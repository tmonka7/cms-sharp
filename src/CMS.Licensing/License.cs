using System;
using System.Collections.Generic;
using System.Linq;

namespace CMS.Licensing;

/// <summary>The capabilities a licence may grant.</summary>
public static class LicenseFeatures
{
    public const string LiveView = "liveview";
    public const string Recording = "recording";
    public const string ObjectDetection = "objectdetection";
    public const string FaceRecognition = "facerecognition";
    public const string Attendance = "attendance";
    public const string PtzControl = "ptzcontrol";

    /// <summary>Key plus label, in the order the generator lists them.</summary>
    public static readonly KeyValuePair<string, string>[] All =
    {
        new KeyValuePair<string, string>(LiveView, "Live View"),
        new KeyValuePair<string, string>(Recording, "Recording and Playback"),
        new KeyValuePair<string, string>(ObjectDetection, "Object Detection"),
        new KeyValuePair<string, string>(FaceRecognition, "Face Recognition"),
        new KeyValuePair<string, string>(Attendance, "Automatic Attendance"),
        new KeyValuePair<string, string>(PtzControl, "PTZ Control")
    };

    public static string Label(string key)
    {
        foreach (var pair in All)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return key;
    }
}

/// <summary>Prepared bundles the generator offers, so editions stay consistent.</summary>
public static class LicenseEditions
{
    public const string Trial = "Trial";
    public const string Standard = "Standard";
    public const string Professional = "Professional";

    public static readonly string[] All = { Trial, Standard, Professional };

    public static IEnumerable<string> FeaturesFor(string edition)
    {
        switch (edition)
        {
            case Professional:
                return LicenseFeatures.All.Select(f => f.Key);

            case Standard:
                return new[]
                {
                    LicenseFeatures.LiveView,
                    LicenseFeatures.Recording,
                    LicenseFeatures.ObjectDetection,
                    LicenseFeatures.FaceRecognition,
                    LicenseFeatures.PtzControl
                };

            default:
                return new[] { LicenseFeatures.LiveView, LicenseFeatures.Recording };
        }
    }

    public static int CamerasFor(string edition) => edition switch
    {
        Professional => 128,
        Standard => 32,
        _ => 4
    };
}

/// <summary>
/// A signed grant to run the product.
///
/// Every field here is covered by the signature. Changing any of them in a
/// licence file invalidates it, which is the whole point: the generator holds
/// the private key and the application only ever holds the public one, so the
/// application can check a licence but cannot produce one.
/// </summary>
public sealed class License
{
    public const string Product = "CameraManagementSystem";

    /// <summary>Identifier printed on the licence, for support and revocation.</summary>
    public string Id { get; set; } = string.Empty;

    public string Customer { get; set; } = string.Empty;

    public string Edition { get; set; } = LicenseEditions.Standard;

    public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Null for a perpetual licence.</summary>
    public DateTime? ExpiresUtc { get; set; }

    /// <summary>
    /// Machine this licence is locked to. Empty means it runs anywhere, which
    /// is what a site or floating licence wants.
    /// </summary>
    public string MachineFingerprint { get; set; } = string.Empty;

    public int MaxCameras { get; set; } = 32;

    public HashSet<string> Features { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string Notes { get; set; } = string.Empty;

    /// <summary>Base64 RSA signature over the canonical payload. Not itself signed.</summary>
    public string Signature { get; set; } = string.Empty;

    public bool IsNodeLocked => !string.IsNullOrWhiteSpace(MachineFingerprint);

    public bool IsPerpetual => !ExpiresUtc.HasValue;

    public bool Grants(string feature) => Features.Contains(feature);

    public int DaysRemaining => ExpiresUtc.HasValue
        ? (int)Math.Ceiling((ExpiresUtc.Value - DateTime.UtcNow).TotalDays)
        : int.MaxValue;

    public string ExpiryText => ExpiresUtc.HasValue
        ? ExpiresUtc.Value.ToLocalTime().ToString("yyyy-MM-dd")
        : "Perpetual";

    public License Clone()
    {
        var copy = (License)MemberwiseClone();
        copy.Features = new HashSet<string>(Features, StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}

/// <summary>Why a licence was refused, or that it was accepted.</summary>
public enum LicenseVerdict
{
    Valid = 0,
    Missing,
    Malformed,
    WrongProduct,
    SignatureInvalid,
    Expired,
    NotYetValid,
    WrongMachine,
    NoPublicKey
}

/// <summary>The outcome of checking a licence, with something to show the operator.</summary>
public sealed class LicenseStatus
{
    private LicenseStatus(LicenseVerdict verdict, string message, License? license)
    {
        Verdict = verdict;
        Message = message;
        License = license;
    }

    public LicenseVerdict Verdict { get; }

    public string Message { get; }

    public License? License { get; }

    public bool IsValid => Verdict == LicenseVerdict.Valid;

    public static LicenseStatus Ok(License license)
        => new LicenseStatus(LicenseVerdict.Valid, "Licensed to " + license.Customer, license);

    public static LicenseStatus Fail(LicenseVerdict verdict, string message, License? license = null)
        => new LicenseStatus(verdict, message, license);

    /// <summary>What the operator should do about it, not just what went wrong.</summary>
    public string Advice => Verdict switch
    {
        LicenseVerdict.Missing =>
            "No licence is installed. Paste a licence key below, or load a licence file supplied with your purchase.",
        LicenseVerdict.Malformed =>
            "The licence could not be read. Check that the whole key was copied, including the last line.",
        LicenseVerdict.WrongProduct =>
            "This licence was issued for a different product.",
        LicenseVerdict.SignatureInvalid =>
            "The licence signature does not match. It may have been edited, or it was issued by a different vendor key.",
        LicenseVerdict.Expired =>
            "The licence period has ended. Contact your supplier for a renewal.",
        LicenseVerdict.NotYetValid =>
            "The licence start date is in the future. Check that this computer's clock is correct.",
        LicenseVerdict.WrongMachine =>
            "This licence is locked to a different computer. Send the installation code below to your supplier for a replacement.",
        LicenseVerdict.NoPublicKey =>
            "This build has no licensing key installed, so no licence can be checked. Contact your supplier.",
        _ => string.Empty
    };
}
