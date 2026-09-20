using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CMS.Core.Onvif;

/// <summary>Raised when a device answers with a SOAP fault.</summary>
public sealed class OnvifFaultException : Exception
{
    public OnvifFaultException(string message, string? subcode = null)
        : base(message) => Subcode = subcode;

    public string? Subcode { get; }
}

/// <summary>
/// Minimal SOAP 1.2 transport for ONVIF. Requests are built as XML documents and
/// signed with a WS-Security UsernameToken digest, which is what virtually every
/// ONVIF device expects.
///
/// Nothing here relies on generated WSDL proxies, so the build and the runtime
/// both stay fully offline.
/// </summary>
public sealed class OnvifSoapClient : IDisposable
{
    private readonly HttpClient _http;
    private bool _disposed;

    static OnvifSoapClient()
    {
        // Cameras almost always present a self-signed certificate. Accepting it
        // is required for HTTPS ONVIF on a closed network; .NET Framework 4.7
        // has no per-handler callback, so it is configured process-wide.
        ServicePointManager.ServerCertificateValidationCallback =
            (sender, certificate, chain, errors) => true;

        ServicePointManager.SecurityProtocol =
            SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

        ServicePointManager.Expect100Continue = false;
        ServicePointManager.DefaultConnectionLimit = 64;
    }

    public OnvifSoapClient(string? username, string? password, TimeSpan? timeout = null)
    {
        Username = username ?? string.Empty;
        Password = password ?? string.Empty;

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            PreAuthenticate = false,
            UseDefaultCredentials = false
        };

        // Some firmware ignores WS-Security and expects HTTP digest instead;
        // offering both means one round trip either way.
        if (!string.IsNullOrEmpty(Username))
        {
            handler.Credentials = new NetworkCredential(Username, Password);
        }

        _http = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(10)
        };
    }

    public string Username { get; }

    public string Password { get; }

    /// <summary>
    /// Sends one SOAP body to a service endpoint and returns the response body
    /// element (the single child of soap:Body).
    /// </summary>
    public async Task<XElement> InvokeAsync(string endpoint, XElement body, CancellationToken cancellationToken = default)
    {
        var envelope = BuildEnvelope(body);
        var xml = envelope.ToString(SaveOptions.DisableFormatting);

        using var content = new StringContent(xml, Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/soap+xml")
        {
            CharSet = "utf-8"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OnvifFaultException(Describe(ex));
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(responseText))
            {
                throw new OnvifFaultException(
                    "Empty response from " + endpoint + " (HTTP " + (int)response.StatusCode + ").");
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(responseText);
            }
            catch (XmlException ex)
            {
                throw new OnvifFaultException("Malformed SOAP response from " + endpoint + ": " + ex.Message);
            }

            var responseBody = document.Root?.Element(OnvifNamespaces.Soap + "Body");
            if (responseBody == null)
            {
                throw new OnvifFaultException("SOAP response contained no Body element.");
            }

            var fault = responseBody.Element(OnvifNamespaces.Soap + "Fault");
            if (fault != null)
            {
                throw BuildFault(fault);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new OnvifFaultException("HTTP " + (int)response.StatusCode + " from " + endpoint + ".");
            }

            var payload = responseBody.Elements().FirstOrDefault();
            if (payload == null)
            {
                throw new OnvifFaultException("SOAP Body was empty.");
            }

            return payload;
        }
    }

    private XElement BuildEnvelope(XElement body)
    {
        var envelope = new XElement(
            OnvifNamespaces.Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", OnvifNamespaces.Soap.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "tds", OnvifNamespaces.Device.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "trt", OnvifNamespaces.Media.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "tptz", OnvifNamespaces.Ptz.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "timg", OnvifNamespaces.Imaging.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "tt", OnvifNamespaces.Schema.NamespaceName));

        if (!string.IsNullOrEmpty(Username))
        {
            envelope.Add(new XElement(OnvifNamespaces.Soap + "Header", BuildSecurityHeader()));
        }

        envelope.Add(new XElement(OnvifNamespaces.Soap + "Body", body));
        return envelope;
    }

    /// <summary>
    /// WS-Security UsernameToken with PasswordDigest:
    /// digest = Base64( SHA1( nonce + created + password ) ).
    /// </summary>
    private XElement BuildSecurityHeader()
    {
        var nonce = CryptoEx.RandomBytes(16);
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var createdBytes = Encoding.UTF8.GetBytes(created);
        var passwordBytes = Encoding.UTF8.GetBytes(Password);

        var buffer = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
        Buffer.BlockCopy(nonce, 0, buffer, 0, nonce.Length);
        Buffer.BlockCopy(createdBytes, 0, buffer, nonce.Length, createdBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, buffer, nonce.Length + createdBytes.Length, passwordBytes.Length);

        var digest = Convert.ToBase64String(CryptoEx.Sha1(buffer));

        return new XElement(
            OnvifNamespaces.WsSecurityExt + "Security",
            new XAttribute(XNamespace.Xmlns + "wsse", OnvifNamespaces.WsSecurityExt.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "wsu", OnvifNamespaces.WsSecurityUtility.NamespaceName),
            new XAttribute(OnvifNamespaces.Soap + "mustUnderstand", "1"),
            new XElement(
                OnvifNamespaces.WsSecurityExt + "UsernameToken",
                new XElement(OnvifNamespaces.WsSecurityExt + "Username", Username),
                new XElement(
                    OnvifNamespaces.WsSecurityExt + "Password",
                    new XAttribute("Type", OnvifNamespaces.PasswordDigestType),
                    digest),
                new XElement(
                    OnvifNamespaces.WsSecurityExt + "Nonce",
                    new XAttribute("EncodingType", OnvifNamespaces.Base64BinaryType),
                    Convert.ToBase64String(nonce)),
                new XElement(OnvifNamespaces.WsSecurityUtility + "Created", created)));
    }

    private static OnvifFaultException BuildFault(XElement fault)
    {
        var reason = fault.Element(OnvifNamespaces.Soap + "Reason")
            ?.Element(OnvifNamespaces.Soap + "Text")?.Value;

        var code = fault.Element(OnvifNamespaces.Soap + "Code");
        var subcode = code?.Element(OnvifNamespaces.Soap + "Subcode")
            ?.Element(OnvifNamespaces.Soap + "Value")?.Value;

        var detail = fault.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Text" && e.Parent != null && e.Parent.Name.LocalName != "Reason")
            ?.Value;

        var message = reason ?? detail ?? subcode ?? "The ONVIF request was rejected by the device.";
        return new OnvifFaultException(message, subcode);
    }

    /// <summary>Turns a transport failure into something an operator can act on.</summary>
    private static string Describe(HttpRequestException exception)
    {
        var inner = exception.InnerException as WebException;
        if (inner != null)
        {
            switch (inner.Status)
            {
                case WebExceptionStatus.ConnectFailure:
                    return "Could not connect to the device. Check the IP address and ONVIF port.";
                case WebExceptionStatus.Timeout:
                    return "The device did not respond in time.";
                case WebExceptionStatus.NameResolutionFailure:
                    return "The host name could not be resolved.";
            }
        }

        return exception.Message;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}
