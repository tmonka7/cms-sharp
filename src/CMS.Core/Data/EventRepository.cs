using System.Data.SQLite;
using System.Globalization;
using System.Text;
using CMS.Core.Models;

namespace CMS.Core.Data;

public sealed class EventRepository
{
    private readonly AppDatabase _database;

    public EventRepository(AppDatabase database) => _database = database;

    public long Insert(EventEntry entry)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO events (timestamp_utc, kind, severity, camera_id, camera_name, message, score, snapshot) " +
            "VALUES (@timestamp, @kind, @severity, @cameraId, @cameraName, @message, @score, @snapshot)";

        command.Parameters.AddWithValue("@timestamp", entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@kind", (int)entry.Kind);
        command.Parameters.AddWithValue("@severity", (int)entry.Severity);
        command.Parameters.AddWithValue("@cameraId", entry.CameraId);
        command.Parameters.AddWithValue("@cameraName", entry.CameraName);
        command.Parameters.AddWithValue("@message", entry.Message);
        command.Parameters.AddWithValue("@score", (object?)entry.Score ?? DBNull.Value);
        command.Parameters.AddWithValue("@snapshot", (object?)entry.Snapshot ?? DBNull.Value);

        entry.Id = AppDatabase.ExecuteInsert(connection, command);
        return entry.Id;
    }

    /// <summary>Filtered page used by the Event Log screen.</summary>
    public List<EventEntry> Query(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        EventKind? kind = null,
        int? cameraId = null,
        int limit = 200,
        int offset = 0,
        bool includeSnapshots = false)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();

        var sql = new StringBuilder(
            "SELECT id, timestamp_utc, kind, severity, camera_id, camera_name, message, score");
        sql.Append(includeSnapshots ? ", snapshot" : ", NULL AS snapshot");
        sql.Append(" FROM events WHERE 1 = 1");

        AppendFilters(sql, command, fromUtc, toUtc, kind, cameraId);

        sql.Append(" ORDER BY timestamp_utc DESC LIMIT @limit OFFSET @offset");
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();
        var result = new List<EventEntry>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public int Count(DateTime? fromUtc = null, DateTime? toUtc = null, EventKind? kind = null, int? cameraId = null)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();

        var sql = new StringBuilder("SELECT COUNT(*) FROM events WHERE 1 = 1");
        AppendFilters(sql, command, fromUtc, toUtc, kind, cameraId);
        command.CommandText = sql.ToString();

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Counts by kind for the dashboard tiles.</summary>
    public Dictionary<EventKind, int> CountByKind(DateTime fromUtc)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT kind, COUNT(*) FROM events WHERE timestamp_utc >= @from GROUP BY kind";
        command.Parameters.AddWithValue("@from", fromUtc.ToString("O", CultureInfo.InvariantCulture));

        using var reader = command.ExecuteReader();
        var result = new Dictionary<EventKind, int>();
        while (reader.Read())
        {
            result[(EventKind)reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return result;
    }

    public byte[]? GetSnapshot(long eventId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot FROM events WHERE id = @id";
        command.Parameters.AddWithValue("@id", eventId);
        return command.ExecuteScalar() as byte[];
    }

    /// <summary>Drops rows older than the configured retention window.</summary>
    public int Purge(DateTime olderThanUtc)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM events WHERE timestamp_utc < @cutoff";
        command.Parameters.AddWithValue("@cutoff", olderThanUtc.ToString("O", CultureInfo.InvariantCulture));
        return command.ExecuteNonQuery();
    }

    private static void AppendFilters(
        StringBuilder sql,
        SQLiteCommand command,
        DateTime? fromUtc,
        DateTime? toUtc,
        EventKind? kind,
        int? cameraId)
    {
        if (fromUtc.HasValue)
        {
            sql.Append(" AND timestamp_utc >= @from");
            command.Parameters.AddWithValue("@from", fromUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        if (toUtc.HasValue)
        {
            sql.Append(" AND timestamp_utc <= @to");
            command.Parameters.AddWithValue("@to", toUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        if (kind.HasValue)
        {
            sql.Append(" AND kind = @kind");
            command.Parameters.AddWithValue("@kind", (int)kind.Value);
        }

        if (cameraId.HasValue && cameraId.Value > 0)
        {
            sql.Append(" AND camera_id = @cameraId");
            command.Parameters.AddWithValue("@cameraId", cameraId.Value);
        }
    }

    private static EventEntry Map(SQLiteDataReader reader)
    {
        var scoreOrdinal = reader.GetOrdinal("score");
        var snapshotOrdinal = reader.GetOrdinal("snapshot");

        return new EventEntry
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            TimestampUtc = CameraRepository.ParseUtc(reader.GetString(reader.GetOrdinal("timestamp_utc"))),
            Kind = (EventKind)reader.GetInt32(reader.GetOrdinal("kind")),
            Severity = (EventSeverity)reader.GetInt32(reader.GetOrdinal("severity")),
            CameraId = reader.GetInt32(reader.GetOrdinal("camera_id")),
            CameraName = reader.GetString(reader.GetOrdinal("camera_name")),
            Message = reader.GetString(reader.GetOrdinal("message")),
            Score = reader.IsDBNull(scoreOrdinal) ? (double?)null : reader.GetDouble(scoreOrdinal),
            Snapshot = reader.IsDBNull(snapshotOrdinal) ? null : (byte[])reader.GetValue(snapshotOrdinal)
        };
    }
}
