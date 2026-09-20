using System.Collections.Concurrent;
using CMS.Core.Models;
using CMS.Core.Onvif;
using OpenCvSharp;

namespace CMS.Core.Streaming;

/// <summary>
/// Owns every running decoder. The UI asks for a camera by id and gets a live
/// stream back; resolving the RTSP URL over ONVIF when needed happens here.
/// </summary>
public sealed class StreamManager : IDisposable
{
    private readonly ConcurrentDictionary<int, CameraStream> _streams = new ConcurrentDictionary<int, CameraStream>();
    private bool _disposed;

    static StreamManager()
    {
        // TCP is far more reliable than UDP for RTSP across switches and Wi-Fi,
        // and avoids the tearing that dropped UDP packets cause.
        Environment.SetEnvironmentVariable(
            "OPENCV_FFMPEG_CAPTURE_OPTIONS",
            "rtsp_transport;tcp|stimeout;10000000|max_delay;500000");
    }

    public event EventHandler<VideoFrame>? FrameReady;

    public event EventHandler<CameraStream>? StatusChanged;

    public event EventHandler<StreamFailure>? ConnectionFailed;

    public IReadOnlyCollection<CameraStream> ActiveStreams => _streams.Values.ToList();

    public int RunningCount => _streams.Values.Count(s => s.IsRunning);

    public CameraStream GetOrCreate(CameraDevice camera)
    {
        return _streams.GetOrAdd(camera.Id, _ =>
        {
            var stream = new CameraStream(camera);

            stream.FrameReady += (sender, frame) =>
            {
                var handler = FrameReady;
                if (handler != null)
                {
                    handler(this, frame);
                }
            };

            stream.StatusChanged += (sender, status) =>
            {
                var handler = StatusChanged;
                if (handler != null)
                {
                    handler(this, (CameraStream)sender!);
                }
            };

            stream.ConnectionFailed += (sender, message) =>
            {
                var handler = ConnectionFailed;
                if (handler != null)
                {
                    handler(this, new StreamFailure(camera, message));
                }
            };

            return stream;
        });
    }

    public CameraStream? Get(int cameraId)
    {
        CameraStream stream;
        return _streams.TryGetValue(cameraId, out stream) ? stream : null;
    }

    /// <summary>
    /// Starts a camera. When the device is configured for ONVIF and has no
    /// explicit URL, the media profile is queried first.
    /// </summary>
    public async Task<CameraStream> StartAsync(CameraDevice camera, CancellationToken cancellationToken = default)
    {
        if (camera.Protocol == CameraProtocol.Onvif && string.IsNullOrWhiteSpace(camera.StreamUrl))
        {
            var url = await ResolveOnvifStreamUrlAsync(camera, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(url))
            {
                camera.StreamUrl = url;
            }
        }

        var stream = GetOrCreate(camera);
        stream.Start();
        return stream;
    }

    /// <summary>Starts a camera without waiting for ONVIF resolution.</summary>
    public CameraStream Start(CameraDevice camera)
    {
        var stream = GetOrCreate(camera);
        stream.Start();
        return stream;
    }

    public void Stop(int cameraId)
    {
        CameraStream stream;
        if (_streams.TryGetValue(cameraId, out stream))
        {
            stream.Stop();
        }
    }

    public void Remove(int cameraId)
    {
        CameraStream stream;
        if (_streams.TryRemove(cameraId, out stream))
        {
            stream.Dispose();
        }
    }

    public void StopAll()
    {
        foreach (var stream in _streams.Values)
        {
            stream.Stop();
        }
    }

    /// <summary>Latest frame for a camera, cloned for the caller.</summary>
    public Mat? Snapshot(int cameraId)
    {
        var stream = Get(cameraId);
        return stream?.GrabLatestFrame();
    }

    /// <summary>Queries the device over ONVIF for a playable RTSP URL.</summary>
    public static async Task<string> ResolveOnvifStreamUrlAsync(
        CameraDevice camera,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var device = new OnvifDeviceClient(
                camera.IpAddress,
                camera.OnvifPort,
                camera.Username,
                camera.Password,
                TimeSpan.FromSeconds(8));

            var profiles = await device.GetProfilesAsync(cancellationToken).ConfigureAwait(false);
            var profile = profiles.FirstOrDefault();

            if (profile == null)
            {
                return string.Empty;
            }

            return await device.GetStreamUriAsync(profile.Token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OnvifFaultException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var stream in _streams.Values)
        {
            stream.Dispose();
        }

        _streams.Clear();
    }
}

/// <summary>A failed connection attempt, reported to the UI and the event log.</summary>
public sealed class StreamFailure
{
    public StreamFailure(CameraDevice camera, string message)
    {
        Camera = camera;
        Message = message;
    }

    public CameraDevice Camera { get; }

    public string Message { get; }
}
