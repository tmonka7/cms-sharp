using System.Globalization;
using System.Xml.Linq;
using CMS.Core.Models;

namespace CMS.Core.Onvif;

/// <summary>
/// ONVIF PTZ operations backing the PTZ Control screen: continuous pan/tilt,
/// zoom, focus, iris, presets and the auto scan / pattern / cruise commands.
/// </summary>
public sealed class OnvifPtzClient
{
    private readonly OnvifDeviceClient _device;

    public OnvifPtzClient(OnvifDeviceClient device) => _device = device;

    /// <summary>Media profile the PTZ commands apply to.</summary>
    public string ProfileToken { get; set; } = string.Empty;

    /// <summary>Video source token, needed by the imaging (focus/iris) service.</summary>
    public string VideoSourceToken { get; set; } = string.Empty;

    /// <summary>Starts a continuous move; call <see cref="StopAsync"/> to halt.</summary>
    public Task MoveAsync(PtzMove direction, float speed = 0.5f, CancellationToken cancellationToken = default)
    {
        if (direction == PtzMove.Stop)
        {
            return StopAsync(cancellationToken);
        }

        float pan;
        float tilt;

        switch (direction)
        {
            case PtzMove.Up: pan = 0f; tilt = speed; break;
            case PtzMove.Down: pan = 0f; tilt = -speed; break;
            case PtzMove.Left: pan = -speed; tilt = 0f; break;
            case PtzMove.Right: pan = speed; tilt = 0f; break;
            case PtzMove.UpLeft: pan = -speed; tilt = speed; break;
            case PtzMove.UpRight: pan = speed; tilt = speed; break;
            case PtzMove.DownLeft: pan = -speed; tilt = -speed; break;
            case PtzMove.DownRight: pan = speed; tilt = -speed; break;
            default: pan = 0f; tilt = 0f; break;
        }

        return ContinuousMoveAsync(pan, tilt, 0f, cancellationToken);
    }

    public Task ZoomAsync(float speed, CancellationToken cancellationToken = default)
        => ContinuousMoveAsync(0f, 0f, speed, cancellationToken);

    public async Task ContinuousMoveAsync(float pan, float tilt, float zoom, CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        var velocity = new XElement(OnvifNamespaces.Ptz + "Velocity");

        if (pan != 0f || tilt != 0f)
        {
            velocity.Add(new XElement(
                OnvifNamespaces.Schema + "PanTilt",
                new XAttribute("x", Format(pan)),
                new XAttribute("y", Format(tilt))));
        }

        if (zoom != 0f)
        {
            velocity.Add(new XElement(
                OnvifNamespaces.Schema + "Zoom",
                new XAttribute("x", Format(zoom))));
        }

        var request = new XElement(
            OnvifNamespaces.Ptz + "ContinuousMove",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken),
            velocity);

        await _device.Soap.InvokeAsync(ptzUrl, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves by a relative step, used for click-to-nudge controls.</summary>
    public async Task RelativeMoveAsync(float pan, float tilt, float zoom, CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        var request = new XElement(
            OnvifNamespaces.Ptz + "RelativeMove",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken),
            new XElement(
                OnvifNamespaces.Ptz + "Translation",
                new XElement(
                    OnvifNamespaces.Schema + "PanTilt",
                    new XAttribute("x", Format(pan)),
                    new XAttribute("y", Format(tilt))),
                new XElement(
                    OnvifNamespaces.Schema + "Zoom",
                    new XAttribute("x", Format(zoom)))));

        await _device.Soap.InvokeAsync(ptzUrl, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        var request = new XElement(
            OnvifNamespaces.Ptz + "Stop",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken),
            new XElement(OnvifNamespaces.Ptz + "PanTilt", "true"),
            new XElement(OnvifNamespaces.Ptz + "Zoom", "true"));

        await _device.Soap.InvokeAsync(ptzUrl, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<OnvifPreset>> GetPresetsAsync(CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        var response = await _device.Soap.InvokeAsync(
            ptzUrl,
            new XElement(
                OnvifNamespaces.Ptz + "GetPresets",
                new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken)),
            cancellationToken).ConfigureAwait(false);

        return response.Elements()
            .Where(e => e.Name.LocalName == "Preset")
            .Select(e => new OnvifPreset
            {
                Token = e.Attribute("token")?.Value ?? string.Empty,
                Name = e.Elements().FirstOrDefault(c => c.Name.LocalName == "Name")?.Value ?? string.Empty
            })
            .ToList();
    }

    public async Task GotoPresetAsync(string presetToken, float speed = 0.7f, CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        var request = new XElement(
            OnvifNamespaces.Ptz + "GotoPreset",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken),
            new XElement(OnvifNamespaces.Ptz + "PresetToken", presetToken),
            new XElement(
                OnvifNamespaces.Ptz + "Speed",
                new XElement(
                    OnvifNamespaces.Schema + "PanTilt",
                    new XAttribute("x", Format(speed)),
                    new XAttribute("y", Format(speed)))));

        await _device.Soap.InvokeAsync(ptzUrl, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stores the current position; returns the token the device assigned.</summary>
    public async Task<string> SetPresetAsync(string presetName, string? presetToken = null, CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        var request = new XElement(
            OnvifNamespaces.Ptz + "SetPreset",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken),
            new XElement(OnvifNamespaces.Ptz + "PresetName", presetName));

        if (!string.IsNullOrEmpty(presetToken))
        {
            request.Add(new XElement(OnvifNamespaces.Ptz + "PresetToken", presetToken));
        }

        var response = await _device.Soap.InvokeAsync(ptzUrl, request, cancellationToken).ConfigureAwait(false);
        return response.Elements().FirstOrDefault(e => e.Name.LocalName == "PresetToken")?.Value ?? string.Empty;
    }

    public async Task RemovePresetAsync(string presetToken, CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        await _device.Soap.InvokeAsync(
            ptzUrl,
            new XElement(
                OnvifNamespaces.Ptz + "RemovePreset",
                new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken),
                new XElement(OnvifNamespaces.Ptz + "PresetToken", presetToken)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends the camera to its configured home position.</summary>
    public async Task GotoHomeAsync(CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        await _device.Soap.InvokeAsync(
            ptzUrl,
            new XElement(
                OnvifNamespaces.Ptz + "GotoHomePosition",
                new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Auto Pan / Auto Scan / Pattern / Cruise map onto ONVIF auxiliary commands,
    /// which is how most PTZ domes expose them.
    /// </summary>
    public async Task SendAuxiliaryAsync(string command, CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        await _device.Soap.InvokeAsync(
            ptzUrl,
            new XElement(
                OnvifNamespaces.Ptz + "SendAuxiliaryCommand",
                new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken),
                new XElement(OnvifNamespaces.Ptz + "AuxiliaryData", command)),
            cancellationToken).ConfigureAwait(false);
    }

    public Task AutoPanAsync(CancellationToken cancellationToken = default)
        => SendAuxiliaryAsync("tt:AutoPan|On", cancellationToken);

    public Task AutoScanAsync(CancellationToken cancellationToken = default)
        => SendAuxiliaryAsync("tt:AutoScan|On", cancellationToken);

    public Task PatternAsync(CancellationToken cancellationToken = default)
        => SendAuxiliaryAsync("tt:Pattern|Run", cancellationToken);

    public Task CruiseAsync(CancellationToken cancellationToken = default)
        => SendAuxiliaryAsync("tt:Tour|Start", cancellationToken);

    /// <summary>Continuous focus; positive moves near, negative moves far.</summary>
    public Task FocusAsync(float speed, CancellationToken cancellationToken = default)
        => ImagingMoveAsync("Focus", speed, cancellationToken);

    /// <summary>
    /// Iris is exposed as an imaging setting rather than a move, so this adjusts
    /// exposure gain on devices that support it.
    /// </summary>
    public async Task IrisAsync(float delta, CancellationToken cancellationToken = default)
    {
        var imagingUrl = await RequireImagingUrlAsync(cancellationToken).ConfigureAwait(false);

        var request = new XElement(
            OnvifNamespaces.Imaging + "SetImagingSettings",
            new XElement(OnvifNamespaces.Imaging + "VideoSourceToken", SourceToken),
            new XElement(
                OnvifNamespaces.Imaging + "ImagingSettings",
                new XElement(
                    OnvifNamespaces.Schema + "Exposure",
                    new XElement(OnvifNamespaces.Schema + "Mode", "MANUAL"),
                    new XElement(OnvifNamespaces.Schema + "Iris", Format(delta)))),
            new XElement(OnvifNamespaces.Imaging + "ForcePersistence", "false"));

        await _device.Soap.InvokeAsync(imagingUrl, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task ImagingMoveAsync(string kind, float speed, CancellationToken cancellationToken)
    {
        var imagingUrl = await RequireImagingUrlAsync(cancellationToken).ConfigureAwait(false);

        var request = new XElement(
            OnvifNamespaces.Imaging + "Move",
            new XElement(OnvifNamespaces.Imaging + "VideoSourceToken", SourceToken),
            new XElement(
                OnvifNamespaces.Imaging + kind,
                new XElement(
                    OnvifNamespaces.Schema + "Continuous",
                    new XElement(OnvifNamespaces.Schema + "Speed", Format(speed)))));

        await _device.Soap.InvokeAsync(imagingUrl, request, cancellationToken).ConfigureAwait(false);
    }

    private string SourceToken
        => string.IsNullOrEmpty(VideoSourceToken) ? ProfileToken : VideoSourceToken;

    private async Task<string> RequirePtzUrlAsync(CancellationToken cancellationToken)
    {
        var url = await _device.EnsurePtzUrlAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(url))
        {
            throw new OnvifFaultException("This camera does not report PTZ support.");
        }

        return url;
    }

    private async Task<string> RequireImagingUrlAsync(CancellationToken cancellationToken)
    {
        var url = _device.Capabilities.ImagingUrl;
        if (string.IsNullOrEmpty(url))
        {
            await _device.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            url = _device.Capabilities.ImagingUrl;
        }

        if (string.IsNullOrEmpty(url))
        {
            throw new OnvifFaultException("This device does not expose the imaging service.");
        }

        return url;
    }


    // ---- Absolute positioning ----
    //
    // The attendance sweep has to go to a known place and come back to it.
    // Continuous and relative moves cannot do that: they carry no reference
    // point, and the mechanical inertia means an identical command covers a
    // different arc each time it is issued.

    /// <summary>Moves to an absolute position in the normalised PTZ space.</summary>
    public async Task AbsoluteMoveAsync(
        PtzPosition position,
        float speed = 0.5f,
        CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        var request = new XElement(
            OnvifNamespaces.Ptz + "AbsoluteMove",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken),
            new XElement(
                OnvifNamespaces.Ptz + "Position",
                new XElement(
                    OnvifNamespaces.Schema + "PanTilt",
                    new XAttribute("x", Format(position.Pan)),
                    new XAttribute("y", Format(position.Tilt))),
                new XElement(
                    OnvifNamespaces.Schema + "Zoom",
                    new XAttribute("x", Format(position.Zoom)))),
            new XElement(
                OnvifNamespaces.Ptz + "Speed",
                new XElement(
                    OnvifNamespaces.Schema + "PanTilt",
                    new XAttribute("x", Format(speed)),
                    new XAttribute("y", Format(speed))),
                new XElement(
                    OnvifNamespaces.Schema + "Zoom",
                    new XAttribute("x", Format(speed)))));

        await _device.Soap.InvokeAsync(ptzUrl, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads where the camera is, and whether it is still moving.</summary>
    public async Task<PtzStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        var response = await _device.Soap.InvokeAsync(
            ptzUrl,
            new XElement(
                OnvifNamespaces.Ptz + "GetStatus",
                new XElement(OnvifNamespaces.Ptz + "ProfileToken", ProfileToken)),
            cancellationToken).ConfigureAwait(false);

        var status = new PtzStatus();
        var descendants = response.Descendants().ToList();

        var panTilt = descendants.FirstOrDefault(e => e.Name.LocalName == "PanTilt" && e.Attribute("x") != null);
        var zoom = descendants.FirstOrDefault(e => e.Name.LocalName == "Zoom" && e.Attribute("x") != null);

        status.Position = new PtzPosition(
            ReadAttribute(panTilt, "x"),
            ReadAttribute(panTilt, "y"),
            ReadAttribute(zoom, "x"));

        var moveStatus = descendants.FirstOrDefault(e => e.Name.LocalName == "MoveStatus");
        if (moveStatus != null)
        {
            status.PanTiltState = moveStatus.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "PanTilt")?.Value ?? string.Empty;
            status.ZoomState = moveStatus.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Zoom")?.Value ?? string.Empty;
        }

        // Not every camera returns MoveStatus. Absence is read as idle rather
        // than as perpetual motion, or the sweep would never take a frame.
        status.IsMoving = IsMovingState(status.PanTiltState) || IsMovingState(status.ZoomState);

        return status;
    }

    /// <summary>
    /// Reads a PTZ node for its mechanical limits. Falls back to the first node
    /// when no token is supplied.
    /// </summary>
    public async Task<PtzNodeInfo> GetNodeAsync(string? nodeToken = null, CancellationToken cancellationToken = default)
    {
        var ptzUrl = await RequirePtzUrlAsync(cancellationToken).ConfigureAwait(false);

        var response = await _device.Soap.InvokeAsync(
            ptzUrl,
            new XElement(OnvifNamespaces.Ptz + "GetNodes"),
            cancellationToken).ConfigureAwait(false);

        var nodes = response.Elements().Where(e => e.Name.LocalName == "PTZNode").ToList();
        if (nodes.Count == 0)
        {
            return new PtzNodeInfo();
        }

        var node = nodes.FirstOrDefault(n =>
                       !string.IsNullOrEmpty(nodeToken) &&
                       string.Equals(n.Attribute("token")?.Value, nodeToken, StringComparison.OrdinalIgnoreCase))
                   ?? nodes[0];

        var info = new PtzNodeInfo
        {
            Token = node.Attribute("token")?.Value ?? string.Empty,
            Name = node.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? string.Empty,
            HasHome = string.Equals(
                node.Elements().FirstOrDefault(e => e.Name.LocalName == "HomeSupported")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase)
        };

        // The absolute space is the one that matters. A camera that does not
        // publish it cannot be positioned repeatably, and the sweep refuses to
        // run rather than producing an arc that drifts on every pass.
        var spaces = node.Descendants().FirstOrDefault(e => e.Name.LocalName == "SupportedPTZSpaces");
        if (spaces != null)
        {
            var absolutePanTilt = spaces.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "AbsolutePanTiltPositionSpace");

            if (absolutePanTilt != null)
            {
                info.SupportsAbsoluteMove = true;
                ReadRange(absolutePanTilt, "XRange", out var panMin, out var panMax);
                ReadRange(absolutePanTilt, "YRange", out var tiltMin, out var tiltMax);

                info.PanMin = panMin;
                info.PanMax = panMax;
                info.TiltMin = tiltMin;
                info.TiltMax = tiltMax;
            }

            var absoluteZoom = spaces.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "AbsoluteZoomPositionSpace");

            if (absoluteZoom != null)
            {
                ReadRange(absoluteZoom, "XRange", out var zoomMin, out var zoomMax);
                info.ZoomMin = zoomMin;
                info.ZoomMax = zoomMax;
            }
        }

        return info;
    }

    private static bool IsMovingState(string state)
        => !string.IsNullOrEmpty(state) && !state.Equals("IDLE", StringComparison.OrdinalIgnoreCase);

    private static float ReadAttribute(XElement? element, string name)
    {
        var raw = element?.Attribute(name)?.Value;
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0f;
    }

    private static void ReadRange(XElement space, string rangeName, out float min, out float max)
    {
        min = -1f;
        max = 1f;

        var range = space.Elements().FirstOrDefault(e => e.Name.LocalName == rangeName);
        if (range == null)
        {
            return;
        }

        var minText = range.Elements().FirstOrDefault(e => e.Name.LocalName == "Min")?.Value;
        var maxText = range.Elements().FirstOrDefault(e => e.Name.LocalName == "Max")?.Value;

        if (float.TryParse(minText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMin))
        {
            min = parsedMin;
        }

        if (float.TryParse(maxText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMax))
        {
            max = parsedMax;
        }
    }

    private static string Format(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
