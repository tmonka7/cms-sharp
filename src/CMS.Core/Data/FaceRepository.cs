using System.Data.SQLite;
using System.Globalization;
using CMS.Core.Models;

namespace CMS.Core.Data;

public sealed class FaceRepository
{
    private readonly AppDatabase _database;

    public FaceRepository(AppDatabase database) => _database = database;

    public List<FaceRecord> GetAll()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM faces ORDER BY id";
        using var reader = command.ExecuteReader();

        var result = new List<FaceRecord>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public int Count()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM faces";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int Insert(FaceRecord record)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO faces (name, face_group, note, registered_utc, thumbnail, embedding, enabled) " +
            "VALUES (@name, @group, @note, @registered, @thumbnail, @embedding, @enabled)";

        Bind(command, record);
        record.Id = (int)AppDatabase.ExecuteInsert(connection, command);
        return record.Id;
    }

    public void Update(FaceRecord record)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE faces SET name = @name, face_group = @group, note = @note, " +
            " thumbnail = @thumbnail, embedding = @embedding, enabled = @enabled " +
            "WHERE id = @id";

        Bind(command, record);
        command.Parameters.AddWithValue("@id", record.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM faces WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SQLiteCommand command, FaceRecord record)
    {
        command.Parameters.AddWithValue("@name", record.Name);
        command.Parameters.AddWithValue("@group", (int)record.Group);
        command.Parameters.AddWithValue("@note", record.Note);
        command.Parameters.AddWithValue("@registered", record.RegisteredUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@thumbnail", (object?)record.Thumbnail ?? DBNull.Value);
        command.Parameters.AddWithValue("@embedding", ToBlob(record.Embedding));
        command.Parameters.AddWithValue("@enabled", record.Enabled ? 1 : 0);
    }

    private static FaceRecord Map(SQLiteDataReader reader)
    {
        var thumbnailOrdinal = reader.GetOrdinal("thumbnail");
        var embeddingOrdinal = reader.GetOrdinal("embedding");

        return new FaceRecord
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Group = (FaceGroup)reader.GetInt32(reader.GetOrdinal("face_group")),
            Note = reader.GetString(reader.GetOrdinal("note")),
            RegisteredUtc = CameraRepository.ParseUtc(reader.GetString(reader.GetOrdinal("registered_utc"))),
            Thumbnail = reader.IsDBNull(thumbnailOrdinal) ? null : (byte[])reader.GetValue(thumbnailOrdinal),
            Embedding = reader.IsDBNull(embeddingOrdinal)
                ? Array.Empty<float>()
                : FromBlob((byte[])reader.GetValue(embeddingOrdinal)),
            Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1
        };
    }

    /// <summary>Embeddings are stored as raw little-endian float32 blobs.</summary>
    private static byte[] ToBlob(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBlob(byte[] blob)
    {
        var values = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, values, 0, values.Length * sizeof(float));
        return values;
    }
}
