using System.Diagnostics;
using CMS.Core.Models;
using OpenCvSharp;

namespace CMS.Core.Streaming;

/// <summary>
/// Decodes one camera on a dedicated background thread and republishes frames.
///
/// Decoding runs through the FFmpeg backend bundled with OpenCvSharp, so an RTSP
/// or ONVIF stream plays with no external player and no internet access.
/// </summary>
public sealed class CameraStream : IDisposable
{
    private readonly object _gate = new object();
    private readonly CameraDevice _camera;
    private CancellationTokenSource? _cancellation;
    private Thread? _worker;
    private Mat? _latestFrame;
    private volatile bool _disposed;

    public CameraStream(CameraDevice camera) => _camera = camera;

    /// <summary>Raised on the decoder thread for every decoded frame.</summary>
    public event EventHandler<VideoFrame>? FrameReady;

    /// <summary>Raised when the connection state changes.</summary>
    public event EventHandler<CameraStatus>? StatusChanged;

    /// <summary>Raised when a connection attempt fails, with the reason.</summary>
    public event EventHandler<string>? ConnectionFailed;

    public CameraDevice Camera => _camera;

    public int CameraId => _camera.Id;

    public bool IsRunning
    {
        get
        {
            var worker = _worker;
            return worker != null && worker.IsAlive;
        }
    }

    public double Fps { get; private set; }

    public long FramesDecoded { get; private set; }

    public string ResolvedUrl { get; private set; } = string.Empty;

    /// <summary>
    /// When true the decoder keeps reconnecting after a drop, which is what a
    /// 24/7 surveillance client needs.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    public int ReconnectDelayMs { get; set; } = 3000;

    /// <summary>Set by the UI so an off-screen camera costs almost nothing.</summary>
    public bool Paused { get; set; }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || IsRunning)
            {
                return;
            }

            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;

            _worker = new Thread(() => Run(token))
            {
                IsBackground = true,
                Name = "rtsp-ch" + _camera.Channel
            };

            _worker.Start();
        }
    }

    public void Stop()
    {
        Thread? worker;

        lock (_gate)
        {
            _cancellation?.Cancel();
            worker = _worker;
            _worker = null;
        }

        if (worker != null && worker.IsAlive && worker != Thread.CurrentThread)
        {
            worker.Join(TimeSpan.FromSeconds(3));
        }

        SetStatus(CameraStatus.Offline);
    }

    /// <summary>Returns a copy of the most recent frame, or null when idle.</summary>
    public Mat? GrabLatestFrame()
    {
        lock (_gate)
        {
            return _latestFrame == null ? null : _latestFrame.Clone();
        }
    }

    private void Run(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            SetStatus(CameraStatus.Connecting);

            VideoCapture? capture = null;
            try
            {
                capture = OpenCapture();

                if (capture == null || !capture.IsOpened())
                {
                    SetStatus(CameraStatus.Error);
                    Raise("Could not open the stream for " + _camera.DisplayName + ".");
                }
                else
                {
                    SetStatus(CameraStatus.Online);
                    PumpFrames(capture, cancellationToken);
                }
            }
            catch (OpenCVException ex)
            {
                SetStatus(CameraStatus.Error);
                Raise(ex.Message);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                SetStatus(CameraStatus.Error);
                Raise(ex.Message);
            }
            finally
            {
                if (capture != null)
                {
                    capture.Release();
                    capture.Dispose();
                }
            }

            if (!WaitBeforeRetry(cancellationToken))
            {
                break;
            }
        }

        SetStatus(CameraStatus.Offline);
    }

    private VideoCapture? OpenCapture()
    {
        if (_camera.Kind == CameraKind.UsbCamera && int.TryParse(_camera.StreamUrl, out var deviceIndex))
        {
            ResolvedUrl = "USB device " + deviceIndex;
            return new VideoCapture(deviceIndex);
        }

        ResolvedUrl = _camera.BuildEffectiveStreamUrl();

        var capture = new VideoCapture(ResolvedUrl, VideoCaptureAPIs.FFMPEG);

        // A small buffer keeps live view close to real time instead of drifting
        // several seconds behind.
        try
        {
            capture.Set(VideoCaptureProperties.BufferSize, 2);
        }
        catch (OpenCVException)
        {
            // Not every backend supports the property; not fatal.
        }

        return capture;
    }

    private void PumpFrames(VideoCapture capture, CancellationToken cancellationToken)
    {
        var frame = new Mat();
        var stopwatch = Stopwatch.StartNew();
        var framesInWindow = 0;
        var emptyReads = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Paused)
                {
                    // Keep draining so the socket buffer does not go stale.
                    capture.Grab();
                    Thread.Sleep(40);
                    continue;
                }

                if (!capture.Read(frame) || frame.Empty())
                {
                    if (++emptyReads > 40)
                    {
                        Raise("The stream for " + _camera.DisplayName + " stalled.");
                        return;
                    }

                    Thread.Sleep(20);
                    continue;
                }

                emptyReads = 0;
                FramesDecoded++;
                framesInWindow++;

                lock (_gate)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = frame.Clone();
                }

                _camera.LastFrameUtc = DateTime.UtcNow;

                var handler = FrameReady;
                if (handler != null)
                {
                    handler(this, new VideoFrame(_camera.Id, frame, FramesDecoded, DateTime.UtcNow));
                }

                if (stopwatch.ElapsedMilliseconds >= 1000)
                {
                    Fps = framesInWindow * 1000.0 / stopwatch.ElapsedMilliseconds;
                    _camera.Fps = Fps;

                    // A rough H.264 estimate: enough to show a live figure in
                    // the UI without decoding container statistics.
                    _camera.BitrateMbps = Math.Round(
                        Fps * frame.Width * frame.Height * 3 / 1_000_000.0 * 0.08, 2);

                    framesInWindow = 0;
                    stopwatch.Restart();
                }
            }
        }
        finally
        {
            frame.Dispose();
        }
    }

    private bool WaitBeforeRetry(CancellationToken cancellationToken)
    {
        if (!AutoReconnect || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return !cancellationToken.WaitHandle.WaitOne(ReconnectDelayMs);
    }

    private void SetStatus(CameraStatus status)
    {
        if (_camera.Status == status)
        {
            return;
        }

        _camera.Status = status;

        var handler = StatusChanged;
        if (handler != null)
        {
            handler(this, status);
        }
    }

    private void Raise(string message)
    {
        var handler = ConnectionFailed;
        if (handler != null)
        {
            handler(this, message);
        }
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
            _cancellation?.Dispose();
            _cancellation = null;
            _latestFrame?.Dispose();
            _latestFrame = null;
        }
    }
}
