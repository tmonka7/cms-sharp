using System.Collections.Concurrent;
using CMS.Core.Ai;
using CMS.Core.Data;
using CMS.Core.Models;
using CMS.Core.Streaming;
using OpenCvSharp;

namespace CMS.Core.Services;

/// <summary>
/// Runs object detection and face recognition over the live streams.
///
/// Inference is far slower than decoding, so frames are sampled on an interval
/// and a camera never has more than one analysis in flight. That keeps live view
/// smooth and bounds memory no matter how many cameras run at once.
/// </summary>
public sealed class AnalyticsEngine : IDisposable
{
    private readonly YoloDetector _detector = new YoloDetector();
    private readonly FaceRecognitionService _faces;
    private readonly EventRepository _events;
    private readonly ConcurrentDictionary<int, CameraAnalyticsState> _state =
        new ConcurrentDictionary<int, CameraAnalyticsState>();
    private readonly SemaphoreSlim _inferenceSlots;
    private AppSettings _settings;
    private bool _disposed;

    public AnalyticsEngine(AppSettings settings, FaceRecognitionService faces, EventRepository events)
    {
        _settings = settings;
        _faces = faces;
        _events = events;

        // Detection and recognition are both CPU-bound. Allowing a couple of
        // concurrent runs uses the machine without starving the UI thread.
        var slots = Math.Max(1, Environment.ProcessorCount / 4);
        _inferenceSlots = new SemaphoreSlim(slots, slots);
    }

    public event EventHandler<DetectionFrameResult>? ObjectsDetected;

    public event EventHandler<FaceFrameResult>? FacesRecognized;

    public event EventHandler<EventEntry>? EventRaised;

    public YoloDetector Detector => _detector;

    public FaceRecognitionService Faces => _faces;

    public bool ObjectModelReady => _detector.IsReady;

    public bool FaceModelReady => _faces.IsReady;

    /// <summary>How long a repeat of the same class is suppressed per camera.</summary>
    public TimeSpan ObjectEventCooldown { get; set; } = TimeSpan.FromSeconds(20);

    public TimeSpan FaceEventCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Loads both model sets from the paths in settings.</summary>
    public void LoadModels(AppSettings settings)
    {
        _settings = settings;

        _detector.ConfidenceThreshold = settings.ObjectConfidenceThreshold;
        _detector.NmsThreshold = settings.ObjectNmsThreshold;
        _detector.ClassFilter = new HashSet<string>(settings.EnabledClasses, StringComparer.OrdinalIgnoreCase);
        _detector.Load(settings.ObjectModelPath, settings.UseGpu);

        _faces.MatchThreshold = settings.FaceMatchThreshold;
        _faces.LoadModels(settings.FaceDetectorPath, settings.FaceEmbedderPath, settings.UseGpu);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _detector.ConfidenceThreshold = settings.ObjectConfidenceThreshold;
        _detector.NmsThreshold = settings.ObjectNmsThreshold;
        _detector.ClassFilter = new HashSet<string>(settings.EnabledClasses, StringComparer.OrdinalIgnoreCase);
        _faces.MatchThreshold = settings.FaceMatchThreshold;
    }

    /// <summary>Attaches to a stream manager so decoded frames get analysed.</summary>
    public void Attach(StreamManager streams) => streams.FrameReady += OnFrameReady;

    public void Detach(StreamManager streams) => streams.FrameReady -= OnFrameReady;

    public void Forget(int cameraId)
    {
        CameraAnalyticsState removed;
        _state.TryRemove(cameraId, out removed);
    }

    /// <summary>Latest results for a camera, used when a view is opened.</summary>
    public CameraAnalyticsSnapshot LatestFor(int cameraId)
    {
        CameraAnalyticsState state;
        return _state.TryGetValue(cameraId, out state)
            ? new CameraAnalyticsSnapshot(state.LastObjects, state.LastFaces)
            : new CameraAnalyticsSnapshot(null, null);
    }

    private void OnFrameReady(object? sender, VideoFrame frame)
    {
        if (_disposed)
        {
            return;
        }

        var state = _state.GetOrAdd(frame.CameraId, id => new CameraAnalyticsState(id));

        var now = Clock.TickCount;
        if (now - state.LastRunTicks < _settings.DetectionIntervalMs)
        {
            return;
        }

        // Skip rather than queue: a backlog would only ever draw stale boxes.
        if (!state.TryBeginRun())
        {
            return;
        }

        state.LastRunTicks = now;

        var manager = sender as StreamManager;
        var stream = manager?.Get(frame.CameraId);
        var camera = stream?.Camera;
        var copy = frame.Image.Clone();

        Task.Run(async () =>
        {
            await _inferenceSlots.WaitAsync().ConfigureAwait(false);
            try
            {
                Analyse(copy, frame.CameraId, camera, state);
            }
            finally
            {
                copy.Dispose();
                _inferenceSlots.Release();
                state.EndRun();
            }
        });
    }

    private void Analyse(Mat frame, int cameraId, CameraDevice? camera, CameraAnalyticsState state)
    {
        try
        {
            var runObjects = _settings.ObjectDetectionEnabled
                && _detector.IsReady
                && (camera == null || camera.ObjectDetectionEnabled);

            if (runObjects)
            {
                var result = _detector.Detect(frame, cameraId);
                state.LastObjects = result;

                ObjectsDetected?.Invoke(this, result);
                RaiseObjectEvents(result, frame, camera, state);
            }

            var runFaces = _settings.FaceRecognitionEnabled
                && _faces.IsReady
                && (camera == null || camera.FaceRecognitionEnabled);

            if (runFaces)
            {
                var result = _faces.Process(frame, cameraId);
                state.LastFaces = result;

                FacesRecognized?.Invoke(this, result);
                RaiseFaceEvents(result, frame, camera, state);
            }
        }
        catch (OpenCVException)
        {
            // A malformed frame must never take the analytics thread down.
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Logs an event the first time a class appears on a camera, then waits out
    /// a cooldown so a person standing in view does not flood the log.
    /// </summary>
    private void RaiseObjectEvents(DetectionFrameResult result, Mat frame, CameraDevice? camera, CameraAnalyticsState state)
    {
        foreach (var detection in result.Detections)
        {
            if (!state.ShouldLogObject(detection.Label, ObjectEventCooldown))
            {
                continue;
            }

            var entry = new EventEntry
            {
                Kind = EventKind.ObjectDetected,
                Severity = EventSeverity.Info,
                CameraId = result.CameraId,
                CameraName = camera != null ? camera.DisplayName : "Camera " + result.CameraId,
                Message = detection.Caption,
                Score = detection.Confidence,
                Snapshot = SafeEncode(frame),
                TimestampUtc = result.TimestampUtc
            };

            _events.Insert(entry);
            EventRaised?.Invoke(this, entry);
        }
    }

    private void RaiseFaceEvents(FaceFrameResult result, Mat frame, CameraDevice? camera, CameraAnalyticsState state)
    {
        foreach (var match in result.Matches)
        {
            if (!match.IsRecognized || match.Record == null)
            {
                continue;
            }

            if (!state.ShouldLogFace(match.Record.Id.ToString(), FaceEventCooldown))
            {
                continue;
            }

            // A blacklisted face is the one alert an operator must not miss.
            var severity = match.Record.Group == FaceGroup.Blacklist
                ? EventSeverity.Critical
                : EventSeverity.Info;

            using var crop = FaceRecognitionService.CropFace(frame, match.Box);

            var entry = new EventEntry
            {
                Kind = EventKind.FaceRecognized,
                Severity = severity,
                CameraId = result.CameraId,
                CameraName = camera != null ? camera.DisplayName : "Camera " + result.CameraId,
                Message = match.DisplayName,
                Score = match.Similarity,
                Snapshot = crop == null ? SafeEncode(frame) : SafeEncode(crop),
                TimestampUtc = result.TimestampUtc
            };

            _events.Insert(entry);
            EventRaised?.Invoke(this, entry);
        }
    }

    /// <summary>Encodes a downscaled JPEG thumbnail for the event row.</summary>
    private static byte[]? SafeEncode(Mat frame)
    {
        try
        {
            var scale = Math.Min(1.0, 320.0 / Math.Max(frame.Width, 1));
            using var thumbnail = new Mat();
            Cv2.Resize(
                frame,
                thumbnail,
                new OpenCvSharp.Size(Math.Max(1, (int)(frame.Width * scale)), Math.Max(1, (int)(frame.Height * scale))));

            Cv2.ImEncode(".jpg", thumbnail, out var buffer, new[] { (int)ImwriteFlags.JpegQuality, 80 });
            return buffer;
        }
        catch (OpenCVException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _detector.Dispose();
        _inferenceSlots.Dispose();
    }

    /// <summary>Per-camera throttling and de-duplication state.</summary>
    private sealed class CameraAnalyticsState
    {
        private readonly Dictionary<string, DateTime> _lastObjectLog =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DateTime> _lastFaceLog =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private int _running;

        public CameraAnalyticsState(int cameraId) => CameraId = cameraId;

        public int CameraId { get; }

        public long LastRunTicks { get; set; }

        public DetectionFrameResult? LastObjects { get; set; }

        public FaceFrameResult? LastFaces { get; set; }

        public bool TryBeginRun() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

        public void EndRun() => Interlocked.Exchange(ref _running, 0);

        public bool ShouldLogObject(string label, TimeSpan cooldown)
            => ShouldLog(_lastObjectLog, label, cooldown);

        public bool ShouldLogFace(string key, TimeSpan cooldown)
            => ShouldLog(_lastFaceLog, key, cooldown);

        private static bool ShouldLog(Dictionary<string, DateTime> log, string key, TimeSpan cooldown)
        {
            lock (log)
            {
                var now = DateTime.UtcNow;

                DateTime last;
                if (log.TryGetValue(key, out last) && now - last < cooldown)
                {
                    return false;
                }

                log[key] = now;
                return true;
            }
        }
    }
}

/// <summary>The most recent analysis results for one camera.</summary>
public readonly struct CameraAnalyticsSnapshot
{
    public CameraAnalyticsSnapshot(DetectionFrameResult? objects, FaceFrameResult? faces)
    {
        Objects = objects;
        Faces = faces;
    }

    public DetectionFrameResult? Objects { get; }

    public FaceFrameResult? Faces { get; }
}
