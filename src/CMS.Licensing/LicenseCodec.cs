using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CMS.Licensing;

/// <summary>
/// Reads and writes the licence text.
///
/// The signature covers a canonical payload rebuilt from the parsed fields in a
/// fixed order, never the raw text as it arrived. Signing the raw text would let
/// anyone reorder lines, change spacing or append fields without disturbing the
/// signature, and then have the application read something other than what was
/// signed.
/// </summary>
public static class LicenseCodec
{
    public const string Header = "CMS-LICENSE-1";
    private const string SignatureField = "signature";

    /// <summary>The exact bytes the signature is computed over.</summary>
    public static string CanonicalPayload(License license)
    {
        var builder = new StringBuilder();

        builder.Append(Header).Append('\n');
        Append(builder, "id", license.Id);
        Append(builder, "product", License.Product);
        Append(builder, "customer", license.Customer);
        Append(builder, "edition", license.Edition);
        Append(builder, "issued", Iso(license.IssuedUtc));
        Append(builder, "expires", license.ExpiresUtc.HasValue ? Iso(license.ExpiresUtc.Value) : string.Empty);
        Append(builder, "machine", license.MachineFingerprint);
        Append(builder, "maxCameras", license.MaxCameras.ToString(CultureInfo.InvariantCulture));

        // Sorted so that two licences granting the same features produce the
        // same payload regardless of the order they were ticked in.
        Append(builder, "features", string.Join(",", license.Features
            .Select(f => f.ToLowerInvariant())
            .OrderBy(f => f, StringComparer.Ordinal)));

        Append(builder, "notes", license.Notes);

        return builder.ToString();
    }

    /// <summary>The full licence text, payload plus signature, as it is distributed.</summary>
    public static string Write(License license)
    {
        var builder = new StringBuilder();
        builder.Append(CanonicalPayload(license));
        builder.Append("--\n");
        Append(builder, SignatureField, license.Signature);
        return builder.ToString();
    }

    /// <summary>
    /// Parses licence text. Returns false rather than throwing, because this
    /// runs on whatever an operator pasted into a text box.
    /// </summary>
    public static bool TryRead(string text, out License license, out string error)
    {
        license = new License();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The licence is empty.";
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sawHeader = false;

        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line == "--")
            {
                continue;
            }

            if (line == Header)
            {
                sawHeader = true;
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line.Substring(0, separator).Trim();
            var value = line.Substring(separator + 1).Trim();

            // First occurrence wins, so a second "machine=" appended after the
            // signed block cannot override the signed one.
            if (!fields.ContainsKey(key))
            {
                fields[key] = Unescape(value);
            }
        }

        if (!sawHeader)
        {
            error = "This does not look like a " + Header + " licence.";
            return false;
        }

        license.Id = Read(fields, "id");
        license.Customer = Read(fields, "customer");
        license.Edition = Read(fields, "edition");
        license.MachineFingerprint = Read(fields, "machine");
        license.Notes = Read(fields, "notes");
        license.Signature = Read(fields, SignatureField);

        if (!TryIso(Read(fields, "issued"), out var issued))
        {
            error = "The issue date is missing or unreadable.";
            return false;
        }

        license.IssuedUtc = issued;

        var expiresText = Read(fields, "expires");
        if (string.IsNullOrEmpty(expiresText))
        {
            license.ExpiresUtc = null;
        }
        else if (TryIso(expiresText, out var expires))
        {
            license.ExpiresUtc = expires;
        }
        else
        {
            error = "The expiry date is unreadable.";
            return false;
        }

        if (int.TryParse(Read(fields, "maxCameras"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cameras))
        {
            license.MaxCameras = cameras;
        }

        license.Features = new HashSet<string>(
            Read(fields, "features")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => f.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        // The product name is not taken from the file: it is a constant in the
        // canonical payload, so a licence naming another product simply fails
        // the signature check rather than being trusted about what it is for.
        var declaredProduct = Read(fields, "product");
        if (!string.IsNullOrEmpty(declaredProduct) &&
            !string.Equals(declaredProduct, License.Product, StringComparison.OrdinalIgnoreCase))
        {
            error = "This licence was issued for " + declaredProduct + ".";
            return false;
        }

        if (string.IsNullOrEmpty(license.Signature))
        {
            error = "The licence has no signature.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Wraps a licence as one long line, for pasting into a field or an e-mail
    /// that would otherwise mangle the line breaks.
    /// </summary>
    public static string ToCompactKey(License license)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(Write(license)));

    /// <summary>Accepts either the block form or the compact single-line form.</summary>
    public static bool TryReadAny(string text, out License license, out string error)
    {
        var trimmed = (text ?? string.Empty).Trim();

        if (trimmed.Length > 0 && !trimmed.StartsWith(Header, StringComparison.Ordinal))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(StripWhitespace(trimmed)));
                if (decoded.StartsWith(Header, StringComparison.Ordinal))
                {
                    return TryRead(decoded, out license, out error);
                }
            }
            catch (FormatException)
            {
                // Not compact form; fall through and try to read it as a block.
            }
        }

        return TryRead(trimmed, out license, out error);
    }

    private static string StripWhitespace(string value)
        => new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static void Append(StringBuilder builder, string key, string value)
        => builder.Append(key).Append('=').Append(Escape(value ?? string.Empty)).Append('\n');

    // Newlines would break the line-per-field format and let a value inject
    // further fields, so they are encoded rather than passed through.
    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\r", string.Empty).Replace("\n", "\\n");

    private static string Unescape(string value)
        => value.Replace("\\n", "\n").Replace("\\\\", "\\");

    private static string Read(IDictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static string Iso(DateTime value)
        => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static bool TryIso(string text, out DateTime value)
    {
        if (DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out value))
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return true;
        }

        value = default;
        return false;
    }
}
