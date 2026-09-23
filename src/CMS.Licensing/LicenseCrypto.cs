using System;
using System.Security.Cryptography;
using System.Text;

namespace CMS.Licensing;

/// <summary>
/// The signing key pair.
///
/// The generator holds the private half and the application ships only the
/// public half. That asymmetry is the entire security argument: the application
/// can tell a genuine licence from a forged one, but nothing in the application
/// can produce a genuine one.
/// </summary>
public sealed class LicenseKeyPair
{
    private LicenseKeyPair(string privateKeyXml, string publicKeyXml)
    {
        PrivateKeyXml = privateKeyXml;
        PublicKeyXml = publicKeyXml;
    }

    /// <summary>RSA-2048. Anything smaller is not worth signing with.</summary>
    public const int KeySize = 2048;

    public string PrivateKeyXml { get; }

    public string PublicKeyXml { get; }

    /// <summary>The public key as a single line, which is how it is embedded.</summary>
    public string PublicKeyCompact => Compact(PublicKeyXml);

    public static LicenseKeyPair Create()
    {
        // The explicit size matters: RSA.Create() on this framework produces a
        // 1024-bit key, and assigning KeySize afterwards does not resize it.
        using var rsa = new RSACryptoServiceProvider(KeySize);
        rsa.PersistKeyInCsp = false;

        return new LicenseKeyPair(rsa.ToXmlString(true), rsa.ToXmlString(false));
    }

    public static LicenseKeyPair FromPrivateKey(string privateKeyXml)
    {
        using var rsa = new RSACryptoServiceProvider();
        rsa.PersistKeyInCsp = false;
        rsa.FromXmlString(Expand(privateKeyXml));

        return new LicenseKeyPair(rsa.ToXmlString(true), rsa.ToXmlString(false));
    }

    public static string Compact(string xml)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));

    public static string Expand(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
    }
}

/// <summary>Signs licences. Only the generator has any business using this.</summary>
public static class LicenseSigner
{
    public static void Sign(License license, string privateKeyXml)
    {
        if (license == null)
        {
            throw new ArgumentNullException(nameof(license));
        }

        using var rsa = new RSACryptoServiceProvider();
        rsa.PersistKeyInCsp = false;
        rsa.FromXmlString(LicenseKeyPair.Expand(privateKeyXml));

        var payload = Encoding.UTF8.GetBytes(LicenseCodec.CanonicalPayload(license));
        license.Signature = Convert.ToBase64String(
            rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
}

/// <summary>Checks licences against the public key. This is what ships.</summary>
public static class LicenseVerifier
{
    /// <summary>
    /// Verifies signature, product, dates and machine binding, in that order.
    /// Signature first: nothing else in the licence means anything until it is
    /// known to be the text that was actually signed.
    /// </summary>
    public static LicenseStatus Verify(License? license, string publicKeyXml, bool checkMachine = true)
    {
        if (license == null)
        {
            return LicenseStatus.Fail(LicenseVerdict.Missing, "No licence is installed.");
        }

        if (string.IsNullOrWhiteSpace(publicKeyXml))
        {
            return LicenseStatus.Fail(LicenseVerdict.NoPublicKey, "This build has no licensing key installed.");
        }

        if (string.IsNullOrWhiteSpace(license.Signature))
        {
            return LicenseStatus.Fail(LicenseVerdict.Malformed, "The licence has no signature.", license);
        }

        bool signatureValid;

        try
        {
            using var rsa = new RSACryptoServiceProvider();
            rsa.PersistKeyInCsp = false;
            rsa.FromXmlString(LicenseKeyPair.Expand(publicKeyXml));

            var payload = Encoding.UTF8.GetBytes(LicenseCodec.CanonicalPayload(license));
            var signature = Convert.FromBase64String(license.Signature);

            signatureValid = rsa.VerifyData(
                payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException || ex is CryptographicException)
        {
            return LicenseStatus.Fail(
                LicenseVerdict.SignatureInvalid, "The licence signature could not be read.", license);
        }

        if (!signatureValid)
        {
            return LicenseStatus.Fail(
                LicenseVerdict.SignatureInvalid, "The licence signature does not match its contents.", license);
        }

        var now = DateTime.UtcNow;

        // A day of slack absorbs a clock that is merely a little out, without
        // accepting a licence dated next year.
        if (license.IssuedUtc > now.AddDays(1))
        {
            return LicenseStatus.Fail(
                LicenseVerdict.NotYetValid,
                "The licence starts on " + license.IssuedUtc.ToLocalTime().ToString("yyyy-MM-dd") + ".",
                license);
        }

        if (license.ExpiresUtc.HasValue && license.ExpiresUtc.Value < now)
        {
            return LicenseStatus.Fail(
                LicenseVerdict.Expired,
                "The licence expired on " + license.ExpiresUtc.Value.ToLocalTime().ToString("yyyy-MM-dd") + ".",
                license);
        }

        if (checkMachine && license.IsNodeLocked && !MachineFingerprint.Matches(license.MachineFingerprint))
        {
            return LicenseStatus.Fail(
                LicenseVerdict.WrongMachine,
                "This licence is locked to another computer.",
                license);
        }

        return LicenseStatus.Ok(license);
    }

    /// <summary>Parses then verifies, for text pasted by an operator.</summary>
    public static LicenseStatus VerifyText(string text, string publicKeyXml, bool checkMachine = true)
    {
        if (!LicenseCodec.TryReadAny(text, out var license, out var error))
        {
            return LicenseStatus.Fail(LicenseVerdict.Malformed, error);
        }

        return Verify(license, publicKeyXml, checkMachine);
    }
}
