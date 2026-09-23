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

/// <summary>
/// A PTZ position in the camera's normalised space, where pan and tilt run from
/// -1 to 1 across the full mechanical range and zoom from 0 to 1.
/// </summary>
public struct PtzPosition
{
    public PtzPosition(float pan, float tilt, float zoom)
    {
        Pan = pan;
        Tilt = tilt;
        Zoom = zoom;
    }

    public float Pan { get; set; }

    public float Tilt { get; set; }

    public float Zoom { get; set; }

    public override string ToString()
        => "pan " + Pan.ToString("0.###") + ", tilt " + Tilt.ToString("0.###") + ", zoom " + Zoom.ToString("0.###");
}

/// <summary>Where the camera is now, and whether it is still moving.</summary>
public sealed class PtzStatus
{
    public PtzPosition Position { get; set; }

    /// <summary>True while any axis reports a state other than IDLE.</summary>
    public bool IsMoving { get; set; }

    public string PanTiltState { get; set; } = string.Empty;

    public string ZoomState { get; set; } = string.Empty;
}

/// <summary>
/// The mechanical limits a PTZ node reports. The pan range is what turns a
/// requested sweep in degrees into normalised positions; without it the same
/// request would cover a different arc on every camera model.
/// </summary>
public sealed class PtzNodeInfo
{
    public string Token { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public float PanMin { get; set; } = -1f;

    public float PanMax { get; set; } = 1f;

    public float TiltMin { get; set; } = -1f;

    public float TiltMax { get; set; } = 1f;

    public float ZoomMin { get; set; }

    public float ZoomMax { get; set; } = 1f;

    /// <summary>
    /// Degrees of pan covered by the full normalised range. ONVIF does not
    /// report this directly, so it defaults to a full revolution and can be
    /// corrected per camera in settings.
    /// </summary>
    public float PanRangeDegrees { get; set; } = 360f;

    public bool SupportsAbsoluteMove { get; set; }

    public bool HasHome { get; set; }

    /// <summary>Normalised units per degree of pan.</summary>
    public float UnitsPerDegree => PanRangeDegrees <= 0f
        ? 0f
        : (PanMax - PanMin) / PanRangeDegrees;
}
