using System.Data.SQLite;
using System.Globalization;
using CMS.Core.Models;

namespace CMS.Core.Data;

public sealed class RecordingRepository
{
    private readonly AppDatabase _database;

    public RecordingRepository(AppDatabase database) => _database = database;

    public long Insert(RecordingSegment segment)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO recordings (camera_id, camera_name, start_utc, end_utc, file_path, size_bytes, has_events) " +
            "VALUES (@cameraId, @cameraName, @start, @end, @path, @size, @hasEvents)";

        command.Parameters.AddWithValue("@cameraId", segment.CameraId);
        command.Parameters.AddWithValue("@cameraName", segment.CameraName);
        command.Parameters.AddWithValue("@start", segment.StartUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@end", segment.EndUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@path", segment.FilePath);
        command.Parameters.AddWithValue("@size", segment.SizeBytes);
        command.Parameters.AddWithValue("@hasEvents", segment.HasEvents ? 1 : 0);

        segment.Id = AppDatabase.ExecuteInsert(connection, command);
        return segment.Id;
    }

    public List<RecordingSegment> Query(int cameraId, DateTime fromUtc, DateTime toUtc)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT * FROM recordings " +
            "WHERE camera_id = @cameraId AND end_utc >= @from AND start_utc <= @to " +
            "ORDER BY start_utc";

        command.Parameters.AddWithValue("@cameraId", cameraId);
        command.Parameters.AddWithValue("@from", fromUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@to", toUtc.ToString("O", CultureInfo.InvariantCulture));

        return Read(command);
    }

    public long TotalSizeBytes()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(size_bytes), 0) FROM recordings";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public List<RecordingSegment> OlderThan(DateTime cutoffUtc)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM recordings WHERE end_utc < @cutoff ORDER BY start_utc";
        command.Parameters.AddWithValue("@cutoff", cutoffUtc.ToString("O", CultureInfo.InvariantCulture));
        return Read(command);
    }

    public void Delete(long id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM recordings WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    private static List<RecordingSegment> Read(SQLiteCommand command)
    {
        using var reader = command.ExecuteReader();
        var result = new List<RecordingSegment>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    private static RecordingSegment Map(SQLiteDataReader reader) => new RecordingSegment
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        CameraId = reader.GetInt32(reader.GetOrdinal("camera_id")),
        CameraName = reader.GetString(reader.GetOrdinal("camera_name")),
        StartUtc = CameraRepository.ParseUtc(reader.GetString(reader.GetOrdinal("start_utc"))),
        EndUtc = CameraRepository.ParseUtc(reader.GetString(reader.GetOrdinal("end_utc"))),
        FilePath = reader.GetString(reader.GetOrdinal("file_path")),
        SizeBytes = reader.GetInt64(reader.GetOrdinal("size_bytes")),
        HasEvents = reader.GetInt32(reader.GetOrdinal("has_events")) == 1
    };
}
