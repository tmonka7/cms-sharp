namespace CMS.Core.Models;

/// <summary>
/// A single configured camera. Persisted in the local SQLite store so the
/// application keeps working with no connection to any cloud service.
/// </summary>
public sealed class CameraDevice
{
    private CameraStatus _status = CameraStatus.Offline;

    public int Id { get; set; }

    /// <summary>Channel number shown in the UI, e.g. "CH1".</summary>
    public int Channel { get; set; }

    public string Name { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; } = 554;

    /// <summary>ONVIF service port. Most devices expose ONVIF on 80.</summary>
    public int OnvifPort { get; set; } = 80;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Explicit RTSP URL. When empty the URL is resolved through ONVIF media
    /// profiles at connect time.
    /// </summary>
    public string StreamUrl { get; set; } = string.Empty;

    public CameraProtocol Protocol { get; set; } = CameraProtocol.Onvif;

    public CameraKind Kind { get; set; } = CameraKind.IpCamera;

    public bool PtzSupported { get; set; }

    public bool ObjectDetectionEnabled { get; set; } = true;

    public bool FaceRecognitionEnabled { get; set; } = true;

    public bool RecordingEnabled { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Raised whenever <see cref="Status"/> changes.</summary>
    public event EventHandler<CameraStatus>? StatusChanged;

    public CameraStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            StatusChanged?.Invoke(this, value);
        }
    }

    public bool IsOnline => _status == CameraStatus.Online;

    public string StatusText => _status switch
    {
        CameraStatus.Online => "Online",
        CameraStatus.Connecting => "Connecting",
        CameraStatus.Error => "Error",
        _ => "Offline"
    };

    public double BitrateMbps { get; set; }

    public double Fps { get; set; }

    public DateTime? LastFrameUtc { get; set; }

    public string ChannelLabel => "CH" + Channel;

    public string DisplayName => "CH" + Channel + " - " + Name;

    public string ProtocolText => Protocol switch
    {
        CameraProtocol.Onvif => "ONVIF",
        CameraProtocol.Http => "HTTP",
        _ => "RTSP"
    };

    public string KindText => Kind switch
    {
        CameraKind.PtzCamera => "PTZ Camera",
        CameraKind.Nvr => "NVR",
        CameraKind.UsbCamera => "USB Camera",
        _ => "IP Camera"
    };

    /// <summary>
    /// Builds the URL handed to the decoder, injecting credentials when the
    /// stream URL does not already carry them.
    /// </summary>
    public string BuildEffectiveStreamUrl()
    {
        var url = string.IsNullOrWhiteSpace(StreamUrl)
            ? "rtsp://" + IpAddress + ":" + Port + "/Streaming/Channels/101"
            : StreamUrl.Trim();

        if (string.IsNullOrEmpty(Username))
        {
            return url;
        }

        var separator = url.IndexOf("://", StringComparison.Ordinal);
        if (separator < 0)
        {
            return url;
        }

        var scheme = url.Substring(0, separator + 3);
        var rest = url.Substring(separator + 3);
        if (rest.IndexOf('@') >= 0)
        {
            return url;
        }

        var user = Uri.EscapeDataString(Username);
        var pass = Uri.EscapeDataString(Password ?? string.Empty);
        return scheme + user + ":" + pass + "@" + rest;
    }

    public CameraDevice Clone() => (CameraDevice)MemberwiseClone();
}
