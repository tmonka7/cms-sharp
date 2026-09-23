using CMS.Core.Ai;
using CMS.Core.Models;
using CMS.Core.Onvif;
using OpenCvSharp;

namespace CMS.Core.Attendance;

/// <summary>How a sweep finished.</summary>
public enum SweepStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4
}

/// <summary>How an attendee was resolved against the gallery.</summary>
public enum AttendanceOutcome
{
    /// <summary>Matched an identity that was already named.</summary>
    Recognised = 0,

    /// <summary>Matched an identity created automatically by an earlier sweep.</summary>
    RecognisedProvisional = 1,

    /// <summary>Nobody matched, so a provisional identity was created.</summary>
    Enrolled = 2
}

/// <summary>
/// One face kept from one stop of a sweep, with the measurements that decided
/// it was worth keeping.
/// </summary>
public sealed class FaceCapture : IDisposable
{
    private bool _disposed;

    /// <summary>The aligned 112x112 crop. Owned by this object.</summary>
    public Mat? Aligned { get; set; }

    /// <summary>JPEG bytes of the aligned crop, for storage and review.</summary>
    public byte[]? Thumbnail { get; set; }

    public float[] Embedding { get; set; } = Array.Empty<float>();

    public FaceQualityReport Quality { get; set; } = new FaceQualityReport();

    public Detection Box { get; set; }

    /// <summary>Which stop of the sweep produced this, counted from zero.</summary>
    public int StopIndex { get; set; }

    /// <summary>Where the camera was pointing, for review and for a second look.</summary>
    public PtzPosition Position { get; set; }

    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Aligned?.Dispose();
        Aligned = null;
    }
}

/// <summary>
/// One person as the sweep saw them: every capture believed to be the same
/// individual, plus the representative chosen from them.
/// </summary>
public sealed class FaceCluster
{
    public List<FaceCapture> Captures { get; } = new List<FaceCapture>();

    /// <summary>The highest-scoring capture, used for the embedding that is stored.</summary>
    public FaceCapture? Best { get; set; }

    /// <summary>Mean of the member embeddings, renormalised. More stable than any one capture.</summary>
    public float[] Centroid { get; set; } = Array.Empty<float>();

    /// <summary>Distinct stops this person was seen at, a rough confidence in the cluster.</summary>
    public int StopSpan => Captures.Select(c => c.StopIndex).Distinct().Count();
}

/// <summary>One position the camera stops at during a sweep.</summary>
public struct SweepStop
{
    public SweepStop(int index, float offsetDegrees, PtzPosition position)
    {
        Index = index;
        OffsetDegrees = offsetDegrees;
        Position = position;
    }

    public int Index { get; }

    /// <summary>Degrees from the centre of the swept arc, negative to the left.</summary>
    public float OffsetDegrees { get; }

    public PtzPosition Position { get; }
}

/// <summary>The stops a sweep will visit, and the reasoning that produced them.</summary>
public sealed class SweepPlan
{
    public List<SweepStop> Stops { get; set; } = new List<SweepStop>();

    public float ArcDegrees { get; set; }

    public float StepDegrees { get; set; }

    public float FieldOfViewDegrees { get; set; }

    public float OverlapFraction { get; set; }

    /// <summary>Where the camera was before the sweep, so it can be put back.</summary>
    public PtzPosition OriginalPosition { get; set; }

    public bool IsUsable => Stops.Count > 0;

    public string Describe()
        => Stops.Count + " stops across " + ArcDegrees.ToString("0") + " degrees, " +
           StepDegrees.ToString("0.0") + " degrees apart, " +
           (OverlapFraction * 100f).ToString("0") + "% overlap at a " +
           FieldOfViewDegrees.ToString("0") + " degree field of view";
}

/// <summary>Progress reported while a sweep runs, for the screen to display.</summary>
public sealed class SweepProgress
{
    public SweepStatus Status { get; set; } = SweepStatus.Running;

    public int StopIndex { get; set; }

    public int StopCount { get; set; }

    public string Stage { get; set; } = string.Empty;

    public int FacesAccepted { get; set; }

    public int FacesRejected { get; set; }

    public float Percent => StopCount <= 0
        ? 0f
        : MathEx.Clamp(StopIndex / (float)StopCount * 100f, 0f, 100f);
}

/// <summary>One attendee produced by a completed sweep.</summary>
public sealed class AttendanceEntry
{
    public string PersonId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public FaceGroup Group { get; set; } = FaceGroup.Unknown;

    public AttendanceOutcome Outcome { get; set; }

    /// <summary>Similarity to the matched identity, on the remapped 0 to 1 scale.</summary>
    public float Similarity { get; set; }

    /// <summary>Quality score of the capture this entry was built from.</summary>
    public float CaptureScore { get; set; }

    public int CaptureCount { get; set; }

    public int StopSpan { get; set; }

    public DateTime SeenUtc { get; set; } = DateTime.UtcNow;

    public PtzPosition Position { get; set; }

    public byte[]? Thumbnail { get; set; }

    /// <summary>
    /// True when the match was close to the threshold, or the capture was
    /// mediocre. Attendance inferred from a face in a crowd is weaker evidence
    /// than a deliberate check-in, so a borderline result is marked rather than
    /// being presented as settled.
    /// </summary>
    public bool NeedsReview { get; set; }

    public string ReviewReason { get; set; } = string.Empty;

    public string OutcomeText => Outcome switch
    {
        AttendanceOutcome.Recognised => "Recognised",
        AttendanceOutcome.RecognisedProvisional => "Seen before",
        _ => "Newly registered"
    };
}

/// <summary>A single run of the automatic attendance sweep.</summary>
public sealed class AttendanceSession
{
    public string Id { get; set; } = string.Empty;

    public int CameraId { get; set; }

    public string CameraName { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedUtc { get; set; }

    public string StartedBy { get; set; } = string.Empty;

    public SweepStatus Status { get; set; } = SweepStatus.Pending;

    public float ArcDegrees { get; set; }

    public int StopCount { get; set; }

    public int FacesAccepted { get; set; }

    public int FacesRejected { get; set; }

    public int PeopleFound { get; set; }

    public int NewlyEnrolled { get; set; }

    public string EmbeddingVersion { get; set; } = FaceEmbeddingVersions.Current;

    public string? Error { get; set; }

    public List<AttendanceEntry> Entries { get; set; } = new List<AttendanceEntry>();

    public TimeSpan Duration => (FinishedUtc ?? DateTime.UtcNow) - StartedUtc;

    public string StatusText => Status switch
    {
        SweepStatus.Running => "Running",
        SweepStatus.Completed => "Completed",
        SweepStatus.Cancelled => "Cancelled",
        SweepStatus.Failed => "Failed",
        _ => "Pending"
    };
}

/// <summary>Everything tunable about a sweep.</summary>
public sealed class SweepOptions
{
    /// <summary>Arc to cover, centred on where the camera is pointing.</summary>
    public float ArcDegrees { get; set; } = 180f;

    /// <summary>
    /// Assumed horizontal field of view at the sweep zoom. ONVIF does not report
    /// this, so it is configurable; too large a value leaves gaps between stops.
    /// </summary>
    public float FieldOfViewDegrees { get; set; } = 30f;

    /// <summary>Fraction of each tile that overlaps its neighbour.</summary>
    public float OverlapFraction { get; set; } = 0.25f;

    /// <summary>Zoom to hold during the sweep, in normalised units.</summary>
    public float Zoom { get; set; }

    public float MoveSpeed { get; set; } = 0.6f;

    /// <summary>How long to let the camera settle after arriving, before capturing.</summary>
    public int SettleMilliseconds { get; set; } = 900;

    /// <summary>Frames examined at each stop. The best face from any of them is kept.</summary>
    public int FramesPerStop { get; set; } = 4;

    public int FrameIntervalMilliseconds { get; set; } = 180;

    /// <summary>Longest a single stop may take before the sweep gives up on it.</summary>
    public int StopTimeoutMilliseconds { get; set; } = 8000;

    /// <summary>
    /// Raw cosine above which two captures in one session are taken to be the
    /// same person. Higher than the gallery match threshold, because captures
    /// within a session share lighting, camera and pose range.
    /// </summary>
    public float ClusterCosine { get; set; } = 0.50f;

    /// <summary>Clusters seen in fewer captures than this are discarded as noise.</summary>
    public int MinimumCapturesPerPerson { get; set; } = 1;

    public bool ReturnToStartPosition { get; set; } = true;

    /// <summary>Register people who match nobody in the gallery.</summary>
    public bool EnrolUnknownFaces { get; set; } = true;

    public FaceQualityPolicy Quality { get; set; } = new FaceQualityPolicy();
}
