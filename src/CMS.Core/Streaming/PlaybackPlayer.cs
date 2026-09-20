using OpenCvSharp;

namespace CMS.Core.Streaming;

/// <summary>
/// Plays a recorded segment from local disk with seek, pause and speed control.
/// Backs the Playback screen and its timeline scrubber.
/// </summary>
public sealed class PlaybackPlayer : IDisposable
{
    private readonly object _gate = new object();
    private VideoCapture? _capture;
    private CancellationTokenSource? _cancellation;
    private Thread? _worker;
    private double _fps = 25;
    private bool _disposed;

    public event EventHandler<Mat>? FrameReady;

    public event EventHandler? PlaybackEnded;

    public event EventHandler<double>? PositionChanged;

    public bool IsPlaying { get; private set; }

    public bool IsPaused { get; private set; }

    public string FilePath { get; private set; } = string.Empty;

    public int TotalFrames { get; private set; }

    public double DurationSeconds => _fps > 0 ? TotalFrames / _fps : 0;

    /// <summary>1.0 is real time; 2.0 plays twice as fast.</summary>
    public double Speed { get; set; } = 1.0;

    public double PositionSeconds
    {
        get
        {
            lock (_gate)
            {
                return _capture == null || _fps <= 0
                    ? 0
                    : _capture.Get(VideoCaptureProperties.PosFrames) / _fps;
            }
        }
    }

    public bool Open(string filePath)
    {
        Stop();

        lock (_gate)
        {
            CloseCapture();

            if (!File.Exists(filePath))
            {
                return false;
            }

            var capture = new VideoCapture(filePath, VideoCaptureAPIs.FFMPEG);
            if (!capture.IsOpened())
            {
                capture.Dispose();
                return false;
            }

            _capture = capture;
            FilePath = filePath;

            _fps = capture.Get(VideoCaptureProperties.Fps);
            if (_fps <= 0 || double.IsNaN(_fps))
            {
                _fps = 25;
            }

            TotalFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);
            return true;
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_capture == null || _disposed)
            {
                return;
            }

            if (IsPaused)
            {
                IsPaused = false;
                return;
            }

            if (IsPlaying)
            {
                return;
            }

            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;

            IsPlaying = true;
            _worker = new Thread(() => Run(token))
            {
                IsBackground = true,
                Name = "playback"
            };

            _worker.Start();
        }
    }

    public void Pause() => IsPaused = true;

    public void TogglePause()
    {
        if (!IsPlaying)
        {
            Play();
            return;
        }

        IsPaused = !IsPaused;
    }

    public void Stop()
    {
        Thread? worker;

        lock (_gate)
        {
            _cancellation?.Cancel();
            worker = _worker;
            _worker = null;
            IsPlaying = false;
            IsPaused = false;
        }

        if (worker != null && worker.IsAlive && worker != Thread.CurrentThread)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>Jumps to a point in the clip.</summary>
    public void Seek(double seconds)
    {
        lock (_gate)
        {
            if (_capture == null)
            {
                return;
            }

            var frame = MathEx.Clamp(seconds * _fps, 0, Math.Max(0, TotalFrames - 1));
            _capture.Set(VideoCaptureProperties.PosFrames, frame);
        }
    }

    /// <summary>Advances exactly one frame, for frame-by-frame review.</summary>
    public void StepFrame()
    {
        Mat? copy = null;

        lock (_gate)
        {
            if (_capture == null)
            {
                return;
            }

            using var frame = new Mat();
            if (_capture.Read(frame) && !frame.Empty())
            {
                copy = frame.Clone();
            }
        }

        if (copy != null)
        {
            using (copy)
            {
                FrameReady?.Invoke(this, copy);
                PositionChanged?.Invoke(this, PositionSeconds);
            }
        }
    }

    private void Run(CancellationToken cancellationToken)
    {
        var frame = new Mat();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (IsPaused)
                {
                    Thread.Sleep(50);
                    continue;
                }

                bool read;
                lock (_gate)
                {
                    read = _capture != null && _capture.Read(frame) && !frame.Empty();
                }

                if (!read)
                {
                    PlaybackEnded?.Invoke(this, EventArgs.Empty);
                    break;
                }

                FrameReady?.Invoke(this, frame);
                PositionChanged?.Invoke(this, PositionSeconds);

                var delay = (int)(1000.0 / (_fps * Math.Max(0.1, Speed)));
                Thread.Sleep(MathEx.Clamp(delay, 1, 500));
            }
        }
        catch (OpenCVException)
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            frame.Dispose();
            IsPlaying = false;
        }
    }

    private void CloseCapture()
    {
        if (_capture == null)
        {
            return;
        }

        _capture.Release();
        _capture.Dispose();
        _capture = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        lock (_gate)
        {
            CloseCapture();
        }
    }
}
