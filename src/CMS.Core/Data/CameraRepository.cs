using System.Data.SQLite;
using System.Globalization;
using CMS.Core.Models;

namespace CMS.Core.Data;

public sealed class CameraRepository
{
    private readonly AppDatabase _database;

    public CameraRepository(AppDatabase database) => _database = database;

    public List<CameraDevice> GetAll()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM cameras ORDER BY channel";
        using var reader = command.ExecuteReader();

        var result = new List<CameraDevice>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public CameraDevice? GetById(int id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM cameras WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public int NextChannel()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(channel), 0) + 1 FROM cameras";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int Insert(CameraDevice camera)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO cameras " +
            "(channel, name, ip_address, port, onvif_port, username, password, stream_url, " +
            " protocol, kind, ptz_supported, object_detection, face_recognition, " +
            " recording_enabled, enabled, created_utc) " +
            "VALUES " +
            "(@channel, @name, @ip, @port, @onvifPort, @username, @password, @streamUrl, " +
            " @protocol, @kind, @ptz, @object, @face, @recording, @enabled, @created)";

        Bind(command, camera);
        camera.Id = (int)AppDatabase.ExecuteInsert(connection, command);
        return camera.Id;
    }

    public void Update(CameraDevice camera)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE cameras SET " +
            " channel = @channel, name = @name, ip_address = @ip, port = @port, " +
            " onvif_port = @onvifPort, username = @username, password = @password, " +
            " stream_url = @streamUrl, protocol = @protocol, kind = @kind, " +
            " ptz_supported = @ptz, object_detection = @object, face_recognition = @face, " +
            " recording_enabled = @recording, enabled = @enabled " +
            "WHERE id = @id";

        Bind(command, camera);
        command.Parameters.AddWithValue("@id", camera.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cameras WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SQLiteCommand command, CameraDevice camera)
    {
        command.Parameters.AddWithValue("@channel", camera.Channel);
        command.Parameters.AddWithValue("@name", camera.Name);
        command.Parameters.AddWithValue("@ip", camera.IpAddress);
        command.Parameters.AddWithValue("@port", camera.Port);
        command.Parameters.AddWithValue("@onvifPort", camera.OnvifPort);
        command.Parameters.AddWithValue("@username", camera.Username);
        command.Parameters.AddWithValue("@password", camera.Password);
        command.Parameters.AddWithValue("@streamUrl", camera.StreamUrl);
        command.Parameters.AddWithValue("@protocol", (int)camera.Protocol);
        command.Parameters.AddWithValue("@kind", (int)camera.Kind);
        command.Parameters.AddWithValue("@ptz", camera.PtzSupported ? 1 : 0);
        command.Parameters.AddWithValue("@object", camera.ObjectDetectionEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@face", camera.FaceRecognitionEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@recording", camera.RecordingEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@enabled", camera.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("@created", camera.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static CameraDevice Map(SQLiteDataReader reader) => new CameraDevice
    {
        Id = reader.GetInt32(reader.GetOrdinal("id")),
        Channel = reader.GetInt32(reader.GetOrdinal("channel")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        IpAddress = reader.GetString(reader.GetOrdinal("ip_address")),
        Port = reader.GetInt32(reader.GetOrdinal("port")),
        OnvifPort = reader.GetInt32(reader.GetOrdinal("onvif_port")),
        Username = reader.GetString(reader.GetOrdinal("username")),
        Password = reader.GetString(reader.GetOrdinal("password")),
        StreamUrl = reader.GetString(reader.GetOrdinal("stream_url")),
        Protocol = (CameraProtocol)reader.GetInt32(reader.GetOrdinal("protocol")),
        Kind = (CameraKind)reader.GetInt32(reader.GetOrdinal("kind")),
        PtzSupported = reader.GetInt32(reader.GetOrdinal("ptz_supported")) == 1,
        ObjectDetectionEnabled = reader.GetInt32(reader.GetOrdinal("object_detection")) == 1,
        FaceRecognitionEnabled = reader.GetInt32(reader.GetOrdinal("face_recognition")) == 1,
        RecordingEnabled = reader.GetInt32(reader.GetOrdinal("recording_enabled")) == 1,
        Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1,
        CreatedUtc = ParseUtc(reader.GetString(reader.GetOrdinal("created_utc")))
    };

    internal static DateTime ParseUtc(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
