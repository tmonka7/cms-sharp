using System.Data.SQLite;
using System.Globalization;
using CMS.Core.Models;

namespace CMS.Core.Data;

public sealed class UserRepository
{
    private readonly AppDatabase _database;

    public UserRepository(AppDatabase database) => _database = database;

    public List<AppUser> GetAll()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM users ORDER BY id";
        using var reader = command.ExecuteReader();

        var result = new List<AppUser>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public AppUser? GetByUsername(string username)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM users WHERE username = @username COLLATE NOCASE";
        command.Parameters.AddWithValue("@username", username);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public int Count()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM users";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int Insert(AppUser user)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO users (username, role, enabled, password_hash, password_salt, permissions, created_utc) " +
            "VALUES (@username, @role, @enabled, @hash, @salt, @permissions, @created)";

        Bind(command, user);
        user.Id = (int)AppDatabase.ExecuteInsert(connection, command);
        return user.Id;
    }

    public void Update(AppUser user)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE users SET username = @username, role = @role, enabled = @enabled, " +
            " password_hash = @hash, password_salt = @salt, permissions = @permissions " +
            "WHERE id = @id";

        Bind(command, user);
        command.Parameters.AddWithValue("@id", user.Id);
        command.ExecuteNonQuery();
    }

    public void TouchLogin(int id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE users SET last_login_utc = @now WHERE id = @id";
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM users WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SQLiteCommand command, AppUser user)
    {
        command.Parameters.AddWithValue("@username", user.Username);
        command.Parameters.AddWithValue("@role", (int)user.Role);
        command.Parameters.AddWithValue("@enabled", user.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("@hash", user.PasswordHash);
        command.Parameters.AddWithValue("@salt", user.PasswordSalt);
        command.Parameters.AddWithValue("@permissions", string.Join(",", user.Permissions));
        command.Parameters.AddWithValue("@created", user.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static AppUser Map(SQLiteDataReader reader)
    {
        var permissions = reader.GetString(reader.GetOrdinal("permissions"));
        var lastLoginOrdinal = reader.GetOrdinal("last_login_utc");

        return new AppUser
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            Username = reader.GetString(reader.GetOrdinal("username")),
            Role = (UserRole)reader.GetInt32(reader.GetOrdinal("role")),
            Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1,
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            PasswordSalt = reader.GetString(reader.GetOrdinal("password_salt")),
            Permissions = new HashSet<string>(
                permissions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase),
            CreatedUtc = CameraRepository.ParseUtc(reader.GetString(reader.GetOrdinal("created_utc"))),
            LastLoginUtc = reader.IsDBNull(lastLoginOrdinal)
                ? (DateTime?)null
                : CameraRepository.ParseUtc(reader.GetString(lastLoginOrdinal))
        };
    }
}
