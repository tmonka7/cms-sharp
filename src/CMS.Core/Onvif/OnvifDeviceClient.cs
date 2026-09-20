using System.Globalization;
using System.Xml.Linq;

namespace CMS.Core.Onvif;

/// <summary>
/// Talks to one ONVIF device: identity, capabilities, media profiles, stream
/// URLs and PTZ. All traffic is LAN-only SOAP over HTTP.
/// </summary>
public sealed class OnvifDeviceClient : IDisposable
{
    private readonly OnvifSoapClient _soap;
    private bool _disposed;

    public OnvifDeviceClient(string host, int port, string username, string password, TimeSpan? timeout = null)
    {
        Host = host;
        Port = port <= 0 ? 80 : port;
        DeviceServiceUrl = "http://" + Host + ":" + Port + "/onvif/device_service";
        _soap = new OnvifSoapClient(username, password, timeout);
    }

    public OnvifDeviceClient(string deviceServiceUrl, string username, string password, TimeSpan? timeout = null)
    {
        DeviceServiceUrl = deviceServiceUrl;
        var uri = new Uri(deviceServiceUrl);
        Host = uri.Host;
        Port = uri.Port;
        _soap = new OnvifSoapClient(username, password, timeout);
    }

    public string Host { get; }

    public int Port { get; }

    public string DeviceServiceUrl { get; }

    public OnvifCapabilities Capabilities { get; private set; } = new OnvifCapabilities();

    internal OnvifSoapClient Soap => _soap;

    /// <summary>
    /// One-shot connect used by Test Connection and by the stream manager:
    /// identity, service URLs, profiles and a playable RTSP URL.
    /// </summary>
    public async Task<OnvifDeviceProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var information = await GetDeviceInformationAsync(cancellationToken).ConfigureAwait(false);
            var capabilities = await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

            var profiles = new List<OnvifProfile>();
            var streamUri = string.Empty;
            var snapshotUri = string.Empty;

            if (capabilities.HasMedia)
            {
                profiles = await GetProfilesAsync(cancellationToken).ConfigureAwait(false);
                var first = profiles.FirstOrDefault();
                if (first != null)
                {
                    streamUri = await GetStreamUriAsync(first.Token, cancellationToken).ConfigureAwait(false);
                    snapshotUri = await TryGetSnapshotUriAsync(first.Token, cancellationToken).ConfigureAwait(false);
                }
            }

            return new OnvifDeviceProbeResult
            {
                Success = true,
                Information = information,
                Capabilities = capabilities,
                Profiles = profiles,
                StreamUri = streamUri,
                SnapshotUri = snapshotUri,
                PtzSupported = capabilities.HasPtz
            };
        }
        catch (Exception ex) when (ex is OnvifFaultException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new OnvifDeviceProbeResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<OnvifDeviceInformation> GetDeviceInformationAsync(CancellationToken cancellationToken = default)
    {
        var response = await _soap.InvokeAsync(
            DeviceServiceUrl,
            new XElement(OnvifNamespaces.Device + "GetDeviceInformation"),
            cancellationToken).ConfigureAwait(false);

        return new OnvifDeviceInformation
        {
            Manufacturer = Read(response, "Manufacturer"),
            Model = Read(response, "Model"),
            FirmwareVersion = Read(response, "FirmwareVersion"),
            SerialNumber = Read(response, "SerialNumber"),
            HardwareId = Read(response, "HardwareId")
        };
    }

    /// <summary>
    /// Resolves service endpoints. Prefers the newer GetServices and falls back
    /// to GetCapabilities for older firmware.
    /// </summary>
    public async Task<OnvifCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = new OnvifCapabilities { DeviceUrl = DeviceServiceUrl };

        try
        {
            var services = await _soap.InvokeAsync(
                DeviceServiceUrl,
                new XElement(
                    OnvifNamespaces.Device + "GetServices",
                    new XElement(OnvifNamespaces.Device + "IncludeCapability", "false")),
                cancellationToken).ConfigureAwait(false);

            foreach (var service in services.Elements().Where(e => e.Name.LocalName == "Service"))
            {
                var ns = service.Elements().FirstOrDefault(e => e.Name.LocalName == "Namespace")?.Value ?? string.Empty;
                var address = service.Elements().FirstOrDefault(e => e.Name.LocalName == "XAddr")?.Value ?? string.Empty;

                if (string.IsNullOrEmpty(address))
                {
                    continue;
                }

                address = Rehost(address);

                if (Has(ns, "/media/wsdl") && string.IsNullOrEmpty(capabilities.MediaUrl))
                {
                    capabilities.MediaUrl = address;
                }
                else if (Has(ns, "/ptz/wsdl"))
                {
                    capabilities.PtzUrl = address;
                }
                else if (Has(ns, "/imaging/wsdl"))
                {
                    capabilities.ImagingUrl = address;
                }
                else if (Has(ns, "/events/wsdl"))
                {
                    capabilities.EventsUrl = address;
                }
            }
        }
        catch (OnvifFaultException)
        {
            // Older devices reject GetServices; the fallback below covers them.
        }

        if (string.IsNullOrEmpty(capabilities.MediaUrl))
        {
            var response = await _soap.InvokeAsync(
                DeviceServiceUrl,
                new XElement(
                    OnvifNamespaces.Device + "GetCapabilities",
                    new XElement(OnvifNamespaces.Device + "Category", "All")),
                cancellationToken).ConfigureAwait(false);

            capabilities.MediaUrl = Rehost(FindSectionAddress(response, "Media"));

            if (string.IsNullOrEmpty(capabilities.PtzUrl))
            {
                capabilities.PtzUrl = Rehost(FindSectionAddress(response, "PTZ"));
            }

            if (string.IsNullOrEmpty(capabilities.ImagingUrl))
            {
                capabilities.ImagingUrl = Rehost(FindSectionAddress(response, "Imaging"));
            }

            if (string.IsNullOrEmpty(capabilities.EventsUrl))
            {
                capabilities.EventsUrl = Rehost(FindSectionAddress(response, "Events"));
            }
        }

        Capabilities = capabilities;
        return capabilities;
    }

    public async Task<List<OnvifProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        var mediaUrl = await EnsureMediaUrlAsync(cancellationToken).ConfigureAwait(false);

        var response = await _soap.InvokeAsync(
            mediaUrl,
            new XElement(OnvifNamespaces.Media + "GetProfiles"),
            cancellationToken).ConfigureAwait(false);

        var profiles = new List<OnvifProfile>();

        foreach (var element in response.Elements().Where(e => e.Name.LocalName == "Profiles"))
        {
            var videoEncoder = Child(element, "VideoEncoderConfiguration");
            var videoSource = Child(element, "VideoSourceConfiguration");
            var resolution = videoEncoder == null ? null : Child(videoEncoder, "Resolution");
            var rateControl = videoEncoder == null ? null : Child(videoEncoder, "RateControl");
            var ptzConfiguration = Child(element, "PTZConfiguration");

            profiles.Add(new OnvifProfile
            {
                Token = element.Attribute("token")?.Value ?? string.Empty,
                Name = Read(element, "Name"),
                Encoding = videoEncoder == null ? string.Empty : Read(videoEncoder, "Encoding"),
                Width = ParseInt(resolution, "Width"),
                Height = ParseInt(resolution, "Height"),
                FrameRate = ParseInt(rateControl, "FrameRateLimit"),
                BitrateKbps = ParseInt(rateControl, "BitrateLimit"),
                VideoSourceToken = videoSource == null ? string.Empty : Read(videoSource, "SourceToken"),
                PtzNodeToken = ptzConfiguration == null ? string.Empty : Read(ptzConfiguration, "NodeToken")
            });
        }

        return profiles;
    }

    /// <summary>Asks the device for the RTSP URL of a profile.</summary>
    public async Task<string> GetStreamUriAsync(string profileToken, CancellationToken cancellationToken = default)
    {
        var mediaUrl = await EnsureMediaUrlAsync(cancellationToken).ConfigureAwait(false);

        var request = new XElement(
            OnvifNamespaces.Media + "GetStreamUri",
            new XElement(
                OnvifNamespaces.Media + "StreamSetup",
                new XElement(OnvifNamespaces.Schema + "Stream", "RTP-Unicast"),
                new XElement(
                    OnvifNamespaces.Schema + "Transport",
                    new XElement(OnvifNamespaces.Schema + "Protocol", "RTSP"))),
            new XElement(OnvifNamespaces.Media + "ProfileToken", profileToken));

        var response = await _soap.InvokeAsync(mediaUrl, request, cancellationToken).ConfigureAwait(false);
        var uri = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "Uri")?.Value ?? string.Empty;
        return RehostStream(uri);
    }

    public async Task<string> TryGetSnapshotUriAsync(string profileToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var mediaUrl = await EnsureMediaUrlAsync(cancellationToken).ConfigureAwait(false);

            var response = await _soap.InvokeAsync(
                mediaUrl,
                new XElement(
                    OnvifNamespaces.Media + "GetSnapshotUri",
                    new XElement(OnvifNamespaces.Media + "ProfileToken", profileToken)),
                cancellationToken).ConfigureAwait(false);

            var uri = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "Uri")?.Value ?? string.Empty;
            return Rehost(uri);
        }
        catch (Exception ex) when (ex is OnvifFaultException or HttpRequestException)
        {
            // Snapshot support is optional; absence is not an error.
            return string.Empty;
        }
    }

    public async Task<DateTime?> GetSystemDateAndTimeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _soap.InvokeAsync(
                DeviceServiceUrl,
                new XElement(OnvifNamespaces.Device + "GetSystemDateAndTime"),
                cancellationToken).ConfigureAwait(false);

            var utc = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "UTCDateTime");
            if (utc == null)
            {
                return null;
            }

            var date = Child(utc, "Date");
            var time = Child(utc, "Time");

            return new DateTime(
                ParseInt(date, "Year"),
                ParseInt(date, "Month"),
                ParseInt(date, "Day"),
                ParseInt(time, "Hour"),
                ParseInt(time, "Minute"),
                ParseInt(time, "Second"),
                DateTimeKind.Utc);
        }
        catch (Exception ex) when (ex is OnvifFaultException or HttpRequestException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public async Task<string> EnsureMediaUrlAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(Capabilities.MediaUrl))
        {
            return Capabilities.MediaUrl;
        }

        var capabilities = await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(capabilities.MediaUrl)
            ? "http://" + Host + ":" + Port + "/onvif/media_service"
            : capabilities.MediaUrl;
    }

    public async Task<string> EnsurePtzUrlAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(Capabilities.PtzUrl))
        {
            return Capabilities.PtzUrl;
        }

        var capabilities = await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        return capabilities.PtzUrl;
    }

    /// <summary>
    /// Devices behind NAT often advertise their internal address. Rewriting the
    /// host to the one we actually reached keeps the URLs usable.
    /// </summary>
    private string Rehost(string url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return new UriBuilder(uri) { Host = Host }.Uri.ToString();
    }

    /// <summary>
    /// Same idea for RTSP, rebuilt by hand because UriBuilder normalises the
    /// port of non-HTTP schemes.
    /// </summary>
    private string RehostStream(string url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
        return uri.Scheme + "://" + Host + port + uri.PathAndQuery;
    }

    private static bool Has(string value, string fragment)
        => value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

    private static XElement? Child(XElement parent, string name)
        => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static string FindSectionAddress(XElement response, string section)
    {
        var element = response.Descendants().FirstOrDefault(e => e.Name.LocalName == section);
        return element?.Elements().FirstOrDefault(e => e.Name.LocalName == "XAddr")?.Value ?? string.Empty;
    }

    private static string Read(XElement parent, string name)
        => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;

    private static int ParseInt(XElement? parent, string name)
    {
        var raw = parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? (int)value
            : 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _soap.Dispose();
    }
}
