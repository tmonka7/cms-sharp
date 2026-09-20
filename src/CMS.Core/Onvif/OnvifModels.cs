namespace CMS.Core.Onvif;

/// <summary>Result of tds:GetDeviceInformation.</summary>
public sealed class OnvifDeviceInformation
{
    public string Manufacturer { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string FirmwareVersion { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string HardwareId { get; set; } = string.Empty;

    public override string ToString() => (Manufacturer + " " + Model).Trim();
}

/// <summary>Service endpoints advertised by tds:GetCapabilities / tds:GetServices.</summary>
public sealed class OnvifCapabilities
{
    public string DeviceUrl { get; set; } = string.Empty;

    public string MediaUrl { get; set; } = string.Empty;

    public string PtzUrl { get; set; } = string.Empty;

    public string ImagingUrl { get; set; } = string.Empty;

    public string EventsUrl { get; set; } = string.Empty;

    public bool HasPtz => !string.IsNullOrEmpty(PtzUrl);

    public bool HasMedia => !string.IsNullOrEmpty(MediaUrl);

    public bool HasImaging => !string.IsNullOrEmpty(ImagingUrl);
}

/// <summary>A media profile; its token selects the stream and the PTZ target.</summary>
public sealed class OnvifProfile
{
    public string Token { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Encoding { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public float FrameRate { get; set; }

    public int BitrateKbps { get; set; }

    public string VideoSourceToken { get; set; } = string.Empty;

    public string PtzNodeToken { get; set; } = string.Empty;

    public string ResolutionText => Width > 0 ? Width + "x" + Height : "unknown";

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? Token
        : Name + " (" + ResolutionText + ")";

    public override string ToString() => DisplayName;
}

/// <summary>A stored PTZ position.</summary>
public sealed class OnvifPreset
{
    public string Token { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Preset " + Token : Name;

    public override string ToString() => DisplayName;
}

/// <summary>Everything the app learns from a device in one connect round trip.</summary>
public sealed class OnvifDeviceProbeResult
{
    public bool Success { get; set; }

    public string? Error { get; set; }

    public OnvifDeviceInformation? Information { get; set; }

    public OnvifCapabilities? Capabilities { get; set; }

    public List<OnvifProfile> Profiles { get; set; } = new List<OnvifProfile>();

    public string StreamUri { get; set; } = string.Empty;

    public string SnapshotUri { get; set; } = string.Empty;

    public bool PtzSupported { get; set; }
}
