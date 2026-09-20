using System.Collections.Concurrent;
using CMS.Core.Data;
using CMS.Core.Models;
using CMS.Core.Streaming;
using OpenCvSharp;

namespace CMS.Core.Services;

/// <summary>
/// Writes live frames to local MP4 segments and registers them so the Playback
/// screen can find them. Recording is local-disk only.
/// </summary>
public sealed class RecordingService : IDisposable
{
    private readonly ConcurrentDictionary<int, CameraRecorder> _recorders =
        new ConcurrentDictionary<int, CameraRecorder>();

    private readonly RecordingRepository _recordings;
    private AppSettings _settings;
    private bool _disposed;

    public RecordingService(RecordingRepository recordings, AppSettings settings)
    {
        _recordings = recordings;
        _settings = settings;
        EnsureStorage();
    }

    /// <summary>Length of one file on disk.</summary>
    public TimeSpan SegmentLength { get; set; } = TimeSpan.FromMinutes(10);

    public event EventHandler<RecordingSegment>? SegmentCompleted;

    public int ActiveCount => _recorders.Count;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        EnsureStorage();
    }

    public bool IsRecording(int cameraId) => _recorders.ContainsKey(cameraId);

    public void Start(CameraDevice camera)
    {
        _recorders.GetOrAdd(
            camera.Id,
            _ => new CameraRecorder(camera, _settings.StorageRoot, SegmentLength, OnSegmentCompleted));
    }

    public void Stop(int cameraId)
    {
        CameraRecorder recorder;
        if (_recorders.TryRemove(cameraId, out recorder))
        {
            recorder.Dispose();
        }
    }

    public bool Toggle(CameraDevice camera)
    {
        if (IsRecording(camera.Id))
        {
            Stop(camera.Id);
            return false;
        }

        Start(camera);
        return true;
    }

    public void StopAll()
    {
        foreach (var id in _recorders.Keys.ToList())
        {
            Stop(id);
        }
    }

    /// <summary>Hook this to <see cref="StreamManager.FrameReady"/>.</summary>
    public void Attach(StreamManager streams) => streams.FrameReady += OnFrameReady;

    public void Detach(StreamManager streams) => streams.FrameReady -= OnFrameReady;

    private void OnFrameReady(object? sender, VideoFrame frame)
    {
        CameraRecorder recorder;
        if (_recorders.TryGetValue(frame.CameraId, out recorder))
        {
            recorder.Write(frame.Image);
        }
    }

    private void OnSegmentCompleted(RecordingSegment segment)
    {
        _recordings.Insert(segment);
        SegmentCompleted?.Invoke(this, segment);
    }

    /// <summary>
    /// Deletes clips past the retention window, then the oldest clips first
    /// while the storage cap is exceeded.
    /// </summary>
    public int ApplyRetentionPolicy()
    {
        var removed = 0;

        foreach (var segment in _recordings.OlderThan(DateTime.UtcNow.AddDays(-_settings.RetentionDays)))
        {
            if (DeleteSegment(segment))
            {
                removed++;
            }
        }

        if (!_settings.OverwriteWhenFull)
        {
            return removed;
        }

        var cap = (long)_settings.MaxStorageGb * 1024 * 1024 * 1024;
        var total = _recordings.TotalSizeBytes();

        if (total <= cap)
        {
            return removed;
        }

        foreach (var segment in _recordings.OlderThan(DateTime.UtcNow))
        {
            if (total <= cap)
            {
                break;
            }

            if (DeleteSegment(segment))
            {
                total -= segment.SizeBytes;
                removed++;
            }
        }

        return removed;
    }

    private bool DeleteSegment(RecordingSegment segment)
    {
        try
        {
            if (File.Exists(segment.FilePath))
            {
                File.Delete(segment.FilePath);
            }

            _recordings.Delete(segment.Id);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void EnsureStorage()
    {
        try
        {
            Directory.CreateDirectory(_settings.StorageRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Surfaced on the Settings screen when the operator saves a path.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAll();
    }

    /// <summary>Owns one open VideoWriter and rolls it over on a schedule.</summary>
    private sealed class CameraRecorder : IDisposable
    {
        private readonly object _gate = new object();
        private readonly CameraDevice _camera;
        private readonly string _root;
        private readonly TimeSpan _segmentLength;
        private readonly Action<RecordingSegment> _onCompleted;

        private VideoWriter? _writer;
        private string _currentPath = string.Empty;
        private DateTime _segmentStartUtc;
        private OpenCvSharp.Size _frameSize;
        private int _framesWritten;

        public CameraRecorder(
            CameraDevice camera,
            string root,
            TimeSpan segmentLength,
            Action<RecordingSegment> onCompleted)
        {
            _camera = camera;
            _root = root;
            _segmentLength = segmentLength;
            _onCompleted = onCompleted;
        }

        public void Write(Mat frame)
        {
            if (frame == null || frame.Empty())
            {
                return;
            }

            lock (_gate)
            {
                if (_writer != null && DateTime.UtcNow - _segmentStartUtc >= _segmentLength)
                {
                    CloseSegment();
                }

                if (_writer == null)
                {
                    OpenSegment(frame.Size());
                }

                if (_writer == null || !_writer.IsOpened())
                {
                    return;
                }

                // A camera can renegotiate resolution mid-stream; the writer
                // cannot, so anything off-size is scaled to the open segment.
                if (frame.Size() != _frameSize)
                {
                    using var resized = new Mat();
                    Cv2.Resize(frame, resized, _frameSize);
                    _writer.Write(resized);
                }
                else
                {
                    _writer.Write(frame);
                }

                _framesWritten++;
            }
        }

        private void OpenSegment(OpenCvSharp.Size size)
        {
            try
            {
                var folder = Path.Combine(
                    _root,
                    "CH" + _camera.Channel,
                    DateTime.Now.ToString("yyyy-MM-dd"));

                Directory.CreateDirectory(folder);

                _frameSize = size;
                _segmentStartUtc = DateTime.UtcNow;
                _framesWritten = 0;
                _currentPath = Path.Combine(folder, DateTime.Now.ToString("HHmmss") + ".mp4");

                _writer = new VideoWriter(_currentPath, FourCC.FromString("mp4v"), 15, size);
            }
            catch (Exception ex) when (ex is OpenCVException or IOException or UnauthorizedAccessException)
            {
                _writer = null;
            }
        }

        private void CloseSegment()
        {
            if (_writer == null)
            {
                return;
            }

            _writer.Release();
            _writer.Dispose();
            _writer = null;

            if (_framesWritten == 0 || string.IsNullOrEmpty(_currentPath))
            {
                return;
            }

            var info = new FileInfo(_currentPath);

            _onCompleted(new RecordingSegment
            {
                CameraId = _camera.Id,
                CameraName = _camera.DisplayName,
                StartUtc = _segmentStartUtc,
                EndUtc = DateTime.UtcNow,
                FilePath = _currentPath,
                SizeBytes = info.Exists ? info.Length : 0
            });
        }

        public void Dispose()
        {
            lock (_gate)
            {
                CloseSegment();
            }
        }
    }
}
