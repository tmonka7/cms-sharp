using System.Globalization;
using CMS.Core.Models;

namespace CMS.Core.Data;

/// <summary>
/// Persists <see cref="AppSettings"/> as plain key/value rows. Storing each
/// field separately avoids a JSON dependency and keeps a half-written value
/// from invalidating the whole configuration.
/// </summary>
public sealed class SettingsRepository
{
    private readonly AppDatabase _database;

    public SettingsRepository(AppDatabase database) => _database = database;

    public AppSettings Load()
    {
        var values = GetAll();
        var settings = new AppSettings();

        settings.SystemName = Read(values, "general.systemName", settings.SystemName);
        settings.Language = Read(values, "general.language", settings.Language);
        settings.TimeZoneId = Read(values, "general.timeZone", settings.TimeZoneId);
        settings.DateFormat = Read(values, "general.dateFormat", settings.DateFormat);
        settings.AutoLogoutMinutes = ReadInt(values, "general.autoLogout", settings.AutoLogoutMinutes);
        settings.StartWithSystem = ReadBool(values, "general.startWithSystem", settings.StartWithSystem);

        settings.OnvifDiscoveryTimeoutSeconds = ReadInt(values, "network.discoveryTimeout", settings.OnvifDiscoveryTimeoutSeconds);
        settings.RtspConnectTimeoutSeconds = ReadInt(values, "network.rtspTimeout", settings.RtspConnectTimeoutSeconds);
        settings.PreferTcpTransport = ReadBool(values, "network.preferTcp", settings.PreferTcpTransport);

        settings.StorageRoot = Read(values, "storage.root", settings.StorageRoot);
        settings.RetentionDays = ReadInt(values, "storage.retentionDays", settings.RetentionDays);
        settings.MaxStorageGb = ReadInt(values, "storage.maxGb", settings.MaxStorageGb);
        settings.OverwriteWhenFull = ReadBool(values, "storage.overwrite", settings.OverwriteWhenFull);

        settings.ObjectModelName = Read(values, "object.modelName", settings.ObjectModelName);
        settings.ObjectModelPath = Read(values, "object.modelPath", settings.ObjectModelPath);
        settings.ObjectConfidenceThreshold = ReadFloat(values, "object.confidence", settings.ObjectConfidenceThreshold);
        settings.ObjectNmsThreshold = ReadFloat(values, "object.nms", settings.ObjectNmsThreshold);
        settings.ObjectDetectionEnabled = ReadBool(values, "object.enabled", settings.ObjectDetectionEnabled);
        settings.DetectionIntervalMs = ReadInt(values, "object.intervalMs", settings.DetectionIntervalMs);

        var classes = Read(values, "object.classes", string.Empty);
        if (!string.IsNullOrWhiteSpace(classes))
        {
            settings.EnabledClasses = new HashSet<string>(
                classes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        settings.FaceDetectorPath = Read(values, "face.detectorPath", settings.FaceDetectorPath);
        settings.FaceEmbedderPath = Read(values, "face.embedderPath", settings.FaceEmbedderPath);
        settings.FaceMatchThreshold = ReadFloat(values, "face.threshold", settings.FaceMatchThreshold);
        settings.FaceRecognitionEnabled = ReadBool(values, "face.enabled", settings.FaceRecognitionEnabled);
        settings.UseGpu = ReadBool(values, "ai.useGpu", settings.UseGpu);

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["general.systemName"] = settings.SystemName,
            ["general.language"] = settings.Language,
            ["general.timeZone"] = settings.TimeZoneId,
            ["general.dateFormat"] = settings.DateFormat,
            ["general.autoLogout"] = settings.AutoLogoutMinutes.ToString(CultureInfo.InvariantCulture),
            ["general.startWithSystem"] = settings.StartWithSystem ? "1" : "0",

            ["network.discoveryTimeout"] = settings.OnvifDiscoveryTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["network.rtspTimeout"] = settings.RtspConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["network.preferTcp"] = settings.PreferTcpTransport ? "1" : "0",

            ["storage.root"] = settings.StorageRoot,
            ["storage.retentionDays"] = settings.RetentionDays.ToString(CultureInfo.InvariantCulture),
            ["storage.maxGb"] = settings.MaxStorageGb.ToString(CultureInfo.InvariantCulture),
            ["storage.overwrite"] = settings.OverwriteWhenFull ? "1" : "0",

            ["object.modelName"] = settings.ObjectModelName,
            ["object.modelPath"] = settings.ObjectModelPath,
            ["object.confidence"] = settings.ObjectConfidenceThreshold.ToString("R", CultureInfo.InvariantCulture),
            ["object.nms"] = settings.ObjectNmsThreshold.ToString("R", CultureInfo.InvariantCulture),
            ["object.enabled"] = settings.ObjectDetectionEnabled ? "1" : "0",
            ["object.intervalMs"] = settings.DetectionIntervalMs.ToString(CultureInfo.InvariantCulture),
            ["object.classes"] = string.Join(",", settings.EnabledClasses),

            ["face.detectorPath"] = settings.FaceDetectorPath,
            ["face.embedderPath"] = settings.FaceEmbedderPath,
            ["face.threshold"] = settings.FaceMatchThreshold.ToString("R", CultureInfo.InvariantCulture),
            ["face.enabled"] = settings.FaceRecognitionEnabled ? "1" : "0",
            ["ai.useGpu"] = settings.UseGpu ? "1" : "0"
        };

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO settings (key, value) VALUES (@key, @value) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value";

        var keyParameter = command.Parameters.Add("@key", System.Data.DbType.String);
        var valueParameter = command.Parameters.Add("@value", System.Data.DbType.String);

        foreach (var pair in values)
        {
            keyParameter.Value = pair.Key;
            valueParameter.Value = pair.Value ?? string.Empty;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public Dictionary<string, string> GetAll()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM settings";

        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    public string? GetValue(string key)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = @key";
        command.Parameters.AddWithValue("@key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetValue(string key, string value)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO settings (key, value) VALUES (@key, @value) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static string Read(IDictionary<string, string> values, string key, string fallback)
        => values.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;

    private static int ReadInt(IDictionary<string, string> values, string key, int fallback)
        => values.TryGetValue(key, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static float ReadFloat(IDictionary<string, string> values, string key, float fallback)
        => values.TryGetValue(key, out var value)
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool ReadBool(IDictionary<string, string> values, string key, bool fallback)
        => values.TryGetValue(key, out var value) ? value == "1" : fallback;
}
