using System.Xml.Linq;

namespace CMS.Core.Onvif;

/// <summary>XML namespaces used by the ONVIF Core, Media, Imaging and PTZ services.</summary>
public static class OnvifNamespaces
{
    public static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    public static readonly XNamespace Addressing = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
    public static readonly XNamespace Discovery = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
    public static readonly XNamespace Device = "http://www.onvif.org/ver10/device/wsdl";
    public static readonly XNamespace Media = "http://www.onvif.org/ver10/media/wsdl";
    public static readonly XNamespace Media2 = "http://www.onvif.org/ver20/media/wsdl";
    public static readonly XNamespace Ptz = "http://www.onvif.org/ver20/ptz/wsdl";
    public static readonly XNamespace Events = "http://www.onvif.org/ver10/events/wsdl";
    public static readonly XNamespace Imaging = "http://www.onvif.org/ver20/imaging/wsdl";
    public static readonly XNamespace Schema = "http://www.onvif.org/ver10/schema";
    public static readonly XNamespace Network = "http://www.onvif.org/ver10/network/wsdl";

    public static readonly XNamespace WsSecurityExt =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";

    public static readonly XNamespace WsSecurityUtility =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

    public const string PasswordDigestType =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest";

    public const string Base64BinaryType =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";
}
