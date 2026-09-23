using CMS.Core.Ai;
using CMS.Core.Data;
using CMS.Core.Data.Mongo;
using CMS.Core.Models;
using CMS.Core.Onvif;
using CMS.Core.Streaming;
using OpenCvSharp;

namespace CMS.Core.Attendance;

/// <summary>
/// Drives one automatic attendance sweep: aim, settle, capture, gate, embed,
/// cluster, match, record.
///
/// The ordering matters more than any single step. Frames are only taken once
/// the camera has stopped, because a moving camera produces motion blur and a
/// blurred face yields an embedding that sits nowhere useful. Captures are
/// clustered before anything is written, because overlapping tiles mean the
/// same person is seen several times and writing each sighting separately would
/// register one person many times over.
/// </summary>
public sealed class AttendanceSweepService
{
    private readonly FaceRecognitionService _faces;
    private readonly StreamManager _streams;
    private readonly IFaceStore _store;
    private readonly MongoAttendanceRepository? _attendance;

    public AttendanceSweepService(
        FaceRecognitionService faces,
        StreamManager streams,
        IFaceStore store,
        MongoAttendanceRepository? attendance)
    {
        _faces = faces;
        _streams = streams;
        _store = store;
        _attendance = attendance;
    }

    /// <summary>Raised on a background thread as the sweep advances.</summary>
    public event EventHandler<SweepProgress>? Progress;

    /// <summary>Raised for each face accepted, so the screen can show it arriving.</summary>
    public event EventHandler<FaceCapture>? FaceAccepted;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Checks everything the sweep depends on before the camera is moved.
    /// Returns null when the sweep can proceed, or the reason it cannot.
    /// </summary>
    public string? Validate(CameraDevice camera, PtzNodeInfo? node)
    {
        if (camera == null)
        {
            return "No camera is selected.";
        }

        if (!camera.PtzSupported)
        {
            return "This camera does not report PTZ support, so it cannot sweep.";
        }

        if (!_faces.IsReady)
        {
            return "The face models are not loaded: " + (_faces.LastError ?? "no detector or recogniser");
        }

        if (!_store.IsAvailable)
        {
            return "The face database is not available: " + (_store.LastError ?? "not connected");
        }

        if (_attendance == null || !_attendance.IsAvailable)
        {
            return "The attendance database is not available.";
        }

        if (node == null || !node.SupportsAbsoluteMove)
        {
            return "This camera does not support absolute positioning, which a repeatable sweep requires.";
        }

        return null;
    }

    /// <summary>
    /// Runs the sweep. The camera is returned to where it started even when the
    /// sweep is cancelled or fails, so an abandoned run does not leave the
    /// camera pointing somewhere unexpected.
    /// </summary>
    public async Task<AttendanceSession> RunAsync(
        CameraDevice camera,
        OnvifPtzClient ptz,
        PtzNodeInfo node,
        SweepOptions options,
        string startedBy,
        CancellationToken cancellationToken = default)
    {
        var session = new AttendanceSession
        {
            CameraId = camera.Id,
            CameraName = camera.DisplayName,
            StartedBy = startedBy,
            Status = SweepStatus.Running,
            ArcDegrees = options.ArcDegrees,
            EmbeddingVersion = FaceEmbeddingVersions.Current
        };

        var captures = new List<FaceCapture>();
        SweepPlan? plan = null;
        IsRunning = true;

        try
        {
            var start = await ptz.GetStatusAsync(cancellationToken).ConfigureAwait(false);

            plan = SweepPlanner.Plan(node, start.Position, options);
            if (!plan.IsUsable)
            {
                throw new InvalidOperationException(
                    "No sweep positions could be planned for this camera.");
            }

            session.StopCount = plan.Stops.Count;
            Report(session, 0, plan.Stops.Count, "Planned " + plan.Describe(), captures.Count, session.FacesRejected);

            foreach (var stop in plan.Stops)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Report(session, stop.Index, plan.Stops.Count,
                    "Moving to " + stop.OffsetDegrees.ToString("0") + " degrees",
                    captures.Count, session.FacesRejected);

                await ptz.AbsoluteMoveAsync(stop.Position, options.MoveSpeed, cancellationToken)
                    .ConfigureAwait(false);

                await SettleAsync(ptz, options, cancellationToken).ConfigureAwait(false);

                Report(session, stop.Index, plan.Stops.Count, "Capturing",
                    captures.Count, session.FacesRejected);

                var found = await CaptureAtStopAsync(camera, stop, options, session, cancellationToken)
                    .ConfigureAwait(false);

                captures.AddRange(found);

                Report(session, stop.Index + 1, plan.Stops.Count,
                    "Captured " + found.Count + " face(s)",
                    captures.Count, session.FacesRejected);
            }

            session.FacesAccepted = captures.Count;

            Report(session, plan.Stops.Count, plan.Stops.Count, "Grouping faces",
                captures.Count, session.FacesRejected);

            var clusters = FaceClusterer.Cluster(captures, options.ClusterCosine)
                .Where(c => c.Captures.Count >= Math.Max(1, options.MinimumCapturesPerPerson))
                .ToList();

            Report(session, plan.Stops.Count, plan.Stops.Count,
                "Matching " + clusters.Count + " person(s)", captures.Count, session.FacesRejected);

            session.Entries = ResolveIdentities(clusters, options, session);
            session.PeopleFound = session.Entries.Count;
            session.NewlyEnrolled = session.Entries.Count(e => e.Outcome == AttendanceOutcome.Enrolled);
            session.Status = SweepStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            session.Status = SweepStatus.Cancelled;
        }
        catch (Exception ex)
        {
            // A sweep touches the camera, two models and two databases. Any of
            // them can fail, and none of those failures should reach the
            // interface as an unhandled fault on a monitoring station.
            session.Status = SweepStatus.Failed;
            session.Error = ex.Message;
        }
        finally
        {
            session.FinishedUtc = DateTime.UtcNow;
            IsRunning = false;

            if (plan != null && options.ReturnToStartPosition)
            {
                await TryReturnAsync(ptz, plan.OriginalPosition, options).ConfigureAwait(false);
            }

            foreach (var capture in captures)
            {
                capture.Dispose();
            }

            Report(session, session.StopCount, session.StopCount,
                session.StatusText, session.FacesAccepted, session.FacesRejected, session.Status);
        }

        TrySave(session);
        return session;
    }

    /// <summary>
    /// Waits for the camera to stop. Cameras that report MoveStatus are polled;
    /// the rest get a fixed settle time, which is why that time is configurable.
    /// </summary>
    private static async Task SettleAsync(
        OnvifPtzClient ptz,
        SweepOptions options,
        CancellationToken cancellationToken)
    {
        var deadline = Clock.TickCount + options.StopTimeoutMilliseconds;

        while (Clock.TickCount < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PtzStatus status;
            try
            {
                status = await ptz.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OnvifFaultException || ex is HttpRequestException)
            {
                // A camera that will not report its status still moves; fall
                // back to waiting rather than abandoning the stop.
                break;
            }

            if (!status.IsMoving)
            {
                break;
            }

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }

        // Even a camera reporting IDLE has residual vibration after a move.
        await Task.Delay(options.SettleMilliseconds, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<FaceCapture>> CaptureAtStopAsync(
        CameraDevice camera,
        SweepStop stop,
        SweepOptions options,
        AttendanceSession session,
        CancellationToken cancellationToken)
    {
        // Keyed by the position of the face in the frame: within one stop the
        // same person appears in every frame examined, and only the best of
        // those is worth keeping.
        var bestByRegion = new Dictionary<string, FaceCapture>(StringComparer.Ordinal);

        for (var frameIndex = 0; frameIndex < Math.Max(1, options.FramesPerStop); frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var frame = _streams.Snapshot(camera.Id);
            if (frame == null || frame.Empty())
            {
                await Task.Delay(options.FrameIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var faces = _faces.DetectFaces(frame);

            foreach (var face in faces)
            {
                var report = FaceQuality.Evaluate(frame, face, options.Quality);

                if (!report.Accepted)
                {
                    session.FacesRejected++;
                    continue;
                }

                var key = RegionKey(face.Box, frame.Width, frame.Height);

                if (bestByRegion.TryGetValue(key, out var existing) &&
                    existing.Quality.Score >= report.Score)
                {
                    continue;
                }

                var aligned = FaceAligner.Align(frame, face.Landmarks);
                if (aligned == null || aligned.Empty())
                {
                    aligned?.Dispose();
                    session.FacesRejected++;
                    continue;
                }

                var embedding = _faces.EmbedAligned(aligned);
                if (embedding.Length == 0)
                {
                    aligned.Dispose();
                    session.FacesRejected++;
                    continue;
                }

                var capture = new FaceCapture
                {
                    Aligned = aligned,
                    Embedding = embedding,
                    Quality = report,
                    Box = face.Box,
                    StopIndex = stop.Index,
                    Position = stop.Position,
                    Thumbnail = FaceRecognitionService.EncodeThumbnail(aligned)
                };

                if (existing != null)
                {
                    existing.Dispose();
                }

                bestByRegion[key] = capture;
            }

            if (frameIndex < options.FramesPerStop - 1)
            {
                await Task.Delay(options.FrameIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        var kept = bestByRegion.Values.ToList();

        foreach (var capture in kept)
        {
            FaceAccepted?.Invoke(this, capture);
        }

        return kept;
    }

    /// <summary>
    /// A coarse grid key. Two detections in the same part of consecutive frames
    /// at the same camera position are the same person, and treating them as
    /// such keeps only the sharpest of the frames examined.
    /// </summary>
    private static string RegionKey(Detection box, int frameWidth, int frameHeight)
    {
        var column = (int)(box.CenterX / Math.Max(1f, frameWidth / 8f));
        var row = (int)(box.CenterY / Math.Max(1f, frameHeight / 8f));
        return column + ":" + row;
    }

    /// <summary>
    /// Turns clusters into attendance entries, enrolling anyone the gallery does
    /// not already hold.
    /// </summary>
    private List<AttendanceEntry> ResolveIdentities(
        List<FaceCluster> clusters,
        SweepOptions options,
        AttendanceSession session)
    {
        var entries = new List<AttendanceEntry>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cluster in clusters)
        {
            if (cluster.Best == null)
            {
                continue;
            }

            var candidate = _faces.FindBestMatch(cluster.Centroid);

            var entry = new AttendanceEntry
            {
                CaptureScore = cluster.Best.Quality.Score,
                CaptureCount = cluster.Captures.Count,
                StopSpan = cluster.StopSpan,
                SeenUtc = cluster.Best.CapturedUtc,
                Position = cluster.Best.Position,
                Thumbnail = cluster.Best.Thumbnail,
                Similarity = candidate.Similarity
            };

            var matched = candidate.Record != null &&
                          candidate.Similarity >= _faces.MatchThreshold &&
                          !claimed.Contains(candidate.Record.DocumentId);

            if (matched && candidate.Record != null)
            {
                entry.PersonId = candidate.Record.DocumentId;
                entry.Name = candidate.Record.Name;
                entry.Group = candidate.Record.Group;
                entry.Outcome = candidate.Record.Provisional
                    ? AttendanceOutcome.RecognisedProvisional
                    : AttendanceOutcome.Recognised;

                claimed.Add(candidate.Record.DocumentId);

                if (_store is MongoFaceRepository mongo)
                {
                    mongo.RecordSighting(candidate.Record, entry.SeenUtc);
                }
            }
            else if (options.EnrolUnknownFaces)
            {
                var record = EnrolProvisional(cluster, session);
                entry.PersonId = record.DocumentId;
                entry.Name = record.Name;
                entry.Group = record.Group;
                entry.Outcome = AttendanceOutcome.Enrolled;
                claimed.Add(record.DocumentId);
            }
            else
            {
                entry.Name = "Unknown";
                entry.Outcome = AttendanceOutcome.Enrolled;
                entry.PersonId = string.Empty;
            }

            MarkForReview(entry, cluster);
            entries.Add(entry);
        }

        return entries
            .OrderByDescending(e => e.Outcome == AttendanceOutcome.Enrolled)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Registers somebody the gallery does not hold, under a generated label.
    /// The record is marked provisional: it will match on later sweeps, but it
    /// makes no claim about who the person is until somebody names it.
    /// </summary>
    private FaceRecord EnrolProvisional(FaceCluster cluster, AttendanceSession session)
    {
        var capture = cluster.Best!;

        var record = new FaceRecord
        {
            Name = ProvisionalName(capture.CapturedUtc),
            Group = FaceGroup.Unknown,
            Note = "Registered automatically from an attendance sweep on " +
                   session.CameraName + ".",
            RegisteredUtc = DateTime.UtcNow,
            LastSeenUtc = capture.CapturedUtc,
            Thumbnail = capture.Thumbnail,

            // The centroid rather than the single best capture: averaging
            // several views of one person is more stable than any one of them.
            Embedding = cluster.Centroid,
            Enabled = true,
            Provisional = true,
            EmbeddingVersion = FaceEmbeddingVersions.Current,
            CaptureCount = cluster.Captures.Count
        };

        record.DocumentId = _store.Insert(record);
        _faces.ReloadGallery();

        return record;
    }

    private static string ProvisionalName(DateTime capturedUtc)
        => "Unknown " + capturedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// Flags an entry a person should check. Attendance read from a face in a
    /// crowd is weaker evidence than a deliberate check-in, and a result near
    /// the threshold is worth a second look rather than silent acceptance.
    /// </summary>
    private void MarkForReview(AttendanceEntry entry, FaceCluster cluster)
    {
        var reasons = new List<string>();

        if (entry.Outcome != AttendanceOutcome.Enrolled)
        {
            var margin = entry.Similarity - _faces.MatchThreshold;
            if (margin < 0.04f)
            {
                reasons.Add("match only " + margin.ToString("0.000") + " above the threshold");
            }
        }

        if (entry.CaptureScore < 0.45f)
        {
            reasons.Add("capture quality " + entry.CaptureScore.ToString("0.00"));
        }

        if (cluster.Captures.Count == 1)
        {
            reasons.Add("seen only once");
        }

        entry.NeedsReview = reasons.Count > 0;
        entry.ReviewReason = string.Join("; ", reasons);
    }

    private static async Task TryReturnAsync(OnvifPtzClient ptz, PtzPosition position, SweepOptions options)
    {
        try
        {
            await ptz.AbsoluteMoveAsync(position, options.MoveSpeed).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OnvifFaultException || ex is HttpRequestException || ex is TaskCanceledException)
        {
            // Returning the camera is a courtesy, not part of the result.
        }
    }

    private void TrySave(AttendanceSession session)
    {
        if (_attendance == null || !_attendance.IsAvailable)
        {
            return;
        }

        try
        {
            _attendance.Save(session);
        }
        catch (Exception ex)
        {
            session.Error = (session.Error == null ? string.Empty : session.Error + " ") +
                            "The session could not be saved: " + ex.Message;
        }
    }

    private void Report(
        AttendanceSession session,
        int stopIndex,
        int stopCount,
        string stage,
        int accepted,
        int rejected,
        SweepStatus? status = null)
    {
        Progress?.Invoke(this, new SweepProgress
        {
            Status = status ?? SweepStatus.Running,
            StopIndex = stopIndex,
            StopCount = stopCount,
            Stage = stage,
            FacesAccepted = accepted,
            FacesRejected = rejected
        });
    }
}
