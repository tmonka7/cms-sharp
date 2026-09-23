using System;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace CMS.Licensing;

/// <summary>
/// A stable identifier for the computer, used to lock a licence to one machine.
///
/// The hard part is not uniqueness, it is stability. A fingerprint that changes
/// after a Windows update or a new network adapter locks a paying customer out
/// of their own system, so only identifiers that survive ordinary maintenance
/// are used: the motherboard UUID and the processor identifier. Network
/// adapters, disk layout and the machine name are deliberately excluded.
/// </summary>
public static class MachineFingerprint
{
    private static readonly object Gate = new object();
    private static string? _cached;
    private static string _sources = string.Empty;

    /// <summary>
    /// The installation code shown to the operator and sent to the supplier.
    /// Formatted in groups so it can be read aloud or typed without error.
    /// </summary>
    public static string Current
    {
        get
        {
            lock (Gate)
            {
                return _cached ??= Compute();
            }
        }
    }

    /// <summary>Which hardware identifiers were readable, for support.</summary>
    public static string Sources
    {
        get
        {
            _ = Current;
            return _sources;
        }
    }

    public static bool Matches(string fingerprint)
        => !string.IsNullOrWhiteSpace(fingerprint) &&
           string.Equals(Normalise(fingerprint), Normalise(Current), StringComparison.OrdinalIgnoreCase);

    /// <summary>Comparison ignores the grouping dashes and case.</summary>
    public static string Normalise(string fingerprint)
        => new string((fingerprint ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string Compute()
    {
        var parts = new System.Collections.Generic.List<string>();
        var found = new System.Collections.Generic.List<string>();

        var uuid = Query("Win32_ComputerSystemProduct", "UUID");
        if (IsUsable(uuid))
        {
            parts.Add("uuid:" + uuid);
            found.Add("board UUID");
        }

        var cpu = Query("Win32_Processor", "ProcessorId");
        if (IsUsable(cpu))
        {
            parts.Add("cpu:" + cpu);
            found.Add("processor ID");
        }

        if (parts.Count == 0)
        {
            // Nothing readable, usually a locked-down or virtualised host. The
            // machine name keeps node locking working rather than failing open,
            // and the source list tells support that this happened.
            parts.Add("name:" + Environment.MachineName);
            found.Add("machine name (hardware identifiers unavailable)");
        }

        _sources = string.Join(", ", found);

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", parts)));

        // 80 bits is ample for identifying a machine and keeps the code short
        // enough to read over the telephone.
        var text = new StringBuilder();
        for (var i = 0; i < 10; i++)
        {
            text.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return Group(text.ToString());
    }

    private static string Group(string value)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                builder.Append('-');
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Some machines report a placeholder UUID rather than a real one; those
    /// must not be treated as identifying, or every such machine would share a
    /// fingerprint.
    /// </summary>
    private static bool IsUsable(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        if (trimmed.All(c => c == '0' || c == '-' || c == 'F' || c == 'f'))
        {
            return false;
        }

        return trimmed != "00000000-0000-0000-0000-000000000000" &&
               trimmed != "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" &&
               trimmed.Length >= 8;
    }

    private static string Query(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT " + property + " FROM " + wmiClass);

            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    var value = item[property];
                    if (value != null)
                    {
                        return value.ToString()!.Trim();
                    }
                }
            }
        }
        catch (Exception ex) when (
            ex is ManagementException ||
            ex is System.Runtime.InteropServices.COMException ||
            ex is UnauthorizedAccessException ||
            ex is System.ComponentModel.Win32Exception)
        {
            // WMI is unavailable or blocked; the caller falls back.
        }

        return string.Empty;
    }
}
