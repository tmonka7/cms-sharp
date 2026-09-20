using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CMS.Core.Onvif;

/// <summary>A camera found on the LAN by WS-Discovery.</summary>
public sealed class DiscoveredDevice
{
    public string EndpointReference { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    /// <summary>The first HTTP service URL advertised by the device.</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    public int Port { get; set; } = 80;

    public string Manufacturer { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = Array.Empty<string>();

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
            {
                return Name;
            }

            var hardware = (Manufacturer + " " + Model).Trim();
            return string.IsNullOrWhiteSpace(hardware) ? Address : hardware;
        }
    }
}

/// <summary>
/// WS-Discovery probe over UDP multicast. It runs entirely on the local network,
/// which is how the product finds cameras with no internet connection.
/// </summary>
public sealed class OnvifDiscovery
{
    private const string MulticastAddress = "239.255.255.250";
    private const int MulticastPort = 3702;

    /// <summary>
    /// Sends a Probe from every usable local interface and collects the
    /// ProbeMatches that arrive within <paramref name="timeout"/>.
    /// </summary>
    public async Task<List<DiscoveredDevice>> DiscoverAsync(
        TimeSpan timeout,
        IProgress<DiscoveredDevice>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        var tasks = new List<Task>();

        foreach (var localAddress in GetLocalAddresses())
        {
            tasks.Add(ProbeOnInterfaceAsync(localAddress, timeout, found, progress, cancellationToken));
        }

        if (tasks.Count == 0)
        {
            return new List<DiscoveredDevice>();
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A cancelled scan still returns whatever already answered.
        }

        lock (found)
        {
            return found.Values.OrderBy(d => d.Address, StringComparer.Ordinal).ToList();
        }
    }

    private static async Task ProbeOnInterfaceAsync(
        IPAddress localAddress,
        TimeSpan timeout,
        Dictionary<string, DiscoveredDevice> found,
        IProgress<DiscoveredDevice>? progress,
        CancellationToken cancellationToken)
    {
        UdpClient client;
        try
        {
            client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(localAddress, 0));
            client.Client.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.MulticastInterface,
                localAddress.GetAddressBytes());
            client.Ttl = 2;
        }
        catch (SocketException)
        {
            return;
        }

        using (client)
        {
            var probe = Encoding.UTF8.GetBytes(BuildProbe("uuid:" + Guid.NewGuid()));
            var target = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);

            try
            {
                // Devices occasionally drop the first datagram, so probe twice.
                await client.SendAsync(probe, probe.Length, target).ConfigureAwait(false);
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                await client.SendAsync(probe, probe.Length, target).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                // .NET Framework has no cancellable ReceiveAsync, so the receive
                // races a timer and the socket is closed by the using block.
                var receive = client.ReceiveAsync();
                var completed = await Task.WhenAny(receive, Task.Delay(remaining, cancellationToken))
                    .ConfigureAwait(false);

                if (completed != receive)
                {
                    break;
                }

                UdpReceiveResult result;
                try
                {
                    result = await receive.ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                var device = ParseProbeMatch(Encoding.UTF8.GetString(result.Buffer), result.RemoteEndPoint.Address);
                if (device == null)
                {
                    continue;
                }

                var key = string.IsNullOrEmpty(device.EndpointReference) ? device.Address : device.EndpointReference;

                var isNew = false;
                lock (found)
                {
                    if (!found.ContainsKey(key))
                    {
                        found[key] = device;
                        isNew = true;
                    }
                }

                if (isNew && progress != null)
                {
                    progress.Report(device);
                }
            }
        }
    }

    private static string BuildProbe(string messageId)
    {
        var envelope = new XElement(
            OnvifNamespaces.Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", OnvifNamespaces.Soap.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", OnvifNamespaces.Addressing.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "d", OnvifNamespaces.Discovery.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dn", OnvifNamespaces.Network.NamespaceName),
            new XElement(
                OnvifNamespaces.Soap + "Header",
                new XElement(OnvifNamespaces.Addressing + "MessageID", messageId),
                new XElement(
                    OnvifNamespaces.Addressing + "To",
                    new XAttribute(OnvifNamespaces.Soap + "mustUnderstand", "1"),
                    "urn:schemas-xmlsoap-org:ws:2005:04:discovery"),
                new XElement(
                    OnvifNamespaces.Addressing + "Action",
                    new XAttribute(OnvifNamespaces.Soap + "mustUnderstand", "1"),
                    "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe")),
            new XElement(
                OnvifNamespaces.Soap + "Body",
                new XElement(
                    OnvifNamespaces.Discovery + "Probe",
                    new XElement(OnvifNamespaces.Discovery + "Types", "dn:NetworkVideoTransmitter"))));

        return envelope.ToString(SaveOptions.DisableFormatting);
    }

    private static DiscoveredDevice? ParseProbeMatch(string xml, IPAddress remote)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return null;
        }

        var match = document.Descendants(OnvifNamespaces.Discovery + "ProbeMatch").FirstOrDefault();
        if (match == null)
        {
            return null;
        }

        var endpoint = match.Element(OnvifNamespaces.Addressing + "EndpointReference")
            ?.Element(OnvifNamespaces.Addressing + "Address")?.Value ?? string.Empty;

        var separator = new[] { ' ', '\r', '\n', '\t' };

        var addresses = (match.Element(OnvifNamespaces.Discovery + "XAddrs")?.Value ?? string.Empty)
            .Split(separator, StringSplitOptions.RemoveEmptyEntries);

        var serviceUrl = addresses.FirstOrDefault(
            a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        var scopes = (match.Element(OnvifNamespaces.Discovery + "Scopes")?.Value ?? string.Empty)
            .Split(separator, StringSplitOptions.RemoveEmptyEntries);

        var host = remote.ToString();
        var port = 80;

        if (!string.IsNullOrEmpty(serviceUrl) && Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            port = uri.Port;
        }

        return new DiscoveredDevice
        {
            EndpointReference = endpoint,
            Address = host,
            Port = port,
            ServiceUrl = serviceUrl,
            Scopes = scopes,
            Manufacturer = ReadScope(scopes, "hardware"),
            Model = ReadScope(scopes, "hardware"),
            Name = ReadScope(scopes, "name")
        };
    }

    /// <summary>
    /// Scopes look like onvif://www.onvif.org/name/MyCamera; this pulls the
    /// value that follows a given key.
    /// </summary>
    private static string ReadScope(IEnumerable<string> scopes, string key)
    {
        var prefix = "onvif://www.onvif.org/" + key + "/";
        var scope = scopes.FirstOrDefault(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return scope == null ? string.Empty : Uri.UnescapeDataString(scope.Substring(prefix.Length));
    }

    private static IEnumerable<IPAddress> GetLocalAddresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                !nic.SupportsMulticast)
            {
                continue;
            }

            foreach (var info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    yield return info.Address;
                }
            }
        }
    }
}
