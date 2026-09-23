using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CMS.Licensing;

/// <summary>
/// Where the generator keeps its signing key.
///
/// The private key is protected with DPAPI under the current Windows account,
/// so a copy of the file is useless on another machine or to another user. It
/// is still a secret on a workstation, not a hardware security module: the
/// operator is expected to keep a backup somewhere safe, because losing it
/// means no further licences can be issued for any installation already using
/// the matching public key.
/// </summary>
public static class SigningKeyStore
{
    private const string FileName = "signing.key";

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CameraManagementSystem",
        "KeyGen");

    public static string Path_ => Path.Combine(Folder, FileName);

    public static bool Exists => File.Exists(Path_);

    public static void Save(LicenseKeyPair keyPair)
    {
        Directory.CreateDirectory(Folder);

        var plain = Encoding.UTF8.GetBytes(keyPair.PrivateKeyXml);
        var sealed_ = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);

        File.WriteAllBytes(Path_, sealed_);
        Array.Clear(plain, 0, plain.Length);
    }

    public static bool TryLoad(out LicenseKeyPair? keyPair, out string error)
    {
        keyPair = null;
        error = string.Empty;

        if (!Exists)
        {
            error = "No signing key has been created yet.";
            return false;
        }

        try
        {
            var sealed_ = File.ReadAllBytes(Path_);
            var plain = ProtectedData.Unprotect(sealed_, null, DataProtectionScope.CurrentUser);
            keyPair = LicenseKeyPair.FromPrivateKey(Encoding.UTF8.GetString(plain));
            Array.Clear(plain, 0, plain.Length);
            return true;
        }
        catch (CryptographicException)
        {
            error = "The signing key could not be decrypted. It belongs to a different Windows account or computer.";
            return false;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
        {
            error = "The signing key could not be read: " + ex.Message;
            return false;
        }
    }

    /// <summary>Writes an unprotected backup the operator can store safely.</summary>
    public static void ExportPrivateKey(LicenseKeyPair keyPair, string path)
        => File.WriteAllText(path, keyPair.PrivateKeyXml, new UTF8Encoding(false));

    public static LicenseKeyPair ImportPrivateKey(string path)
        => LicenseKeyPair.FromPrivateKey(File.ReadAllText(path));
}

/// <summary>
/// Where the application keeps the licence it was given.
///
/// Stored beside the database rather than in the program folder, so that a
/// machine-wide install does not need write access to Program Files, and so
/// that replacing the application folder on an upgrade does not remove the
/// customer's licence.
/// </summary>
public static class LicenseStore
{
    private const string FileName = "license.lic";

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CameraManagementSystem");

    public static string Path_ => Path.Combine(Folder, FileName);

    public static bool Exists => File.Exists(Path_);

    /// <summary>Reads the installed licence text, or empty when there is none.</summary>
    public static string Read()
    {
        try
        {
            return Exists ? File.ReadAllText(Path_) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public static void Write(string licenseText)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path_, licenseText, new UTF8Encoding(false));
    }

    public static void Remove()
    {
        try
        {
            if (Exists)
            {
                File.Delete(Path_);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Nothing useful to do; the caller re-checks and reports.
        }
    }
}

/// <summary>
/// The public key this build checks licences against.
///
/// Embedding it is what makes the check meaningful. When the constant is left
/// empty the application falls back to a <c>license.pub</c> file beside the
/// executable, which is convenient while setting the product up but is not the
/// state to ship in: anyone who can replace that file can install their own
/// signing key and mint licences. Paste the key from the generator into
/// <see cref="Embedded"/> before building for release.
/// </summary>
public static class LicensePublicKey
{
    /// <summary>Base64 of the RSA public key XML. Empty in an unconfigured build.</summary>
    public const string Embedded = "";

    private const string SideFileName = "license.pub";

    public static string SideFilePath => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, SideFileName);

    /// <summary>True when the key came from a file rather than the binary.</summary>
    public static bool IsFromSideFile { get; private set; }

    public static string Resolve()
    {
        if (!string.IsNullOrWhiteSpace(Embedded))
        {
            IsFromSideFile = false;
            return Embedded;
        }

        try
        {
            if (File.Exists(SideFilePath))
            {
                var text = File.ReadAllText(SideFilePath).Trim();
                if (text.Length > 0)
                {
                    IsFromSideFile = true;
                    return text;
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Treated as no key at all, which fails closed.
        }

        IsFromSideFile = false;
        return string.Empty;
    }

    public static void WriteSideFile(string publicKeyCompact)
        => File.WriteAllText(SideFilePath, publicKeyCompact, new UTF8Encoding(false));
}
