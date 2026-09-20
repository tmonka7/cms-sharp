using System.Data.SQLite;

namespace CMS.Core.Data;

/// <summary>
/// Owns the local SQLite file and its schema. Everything the product stores —
/// devices, users, faces, events, recordings, settings — lives here, which is
/// what keeps the whole system usable with no network at all.
/// </summary>
public sealed class AppDatabase
{
    private readonly string _connectionString;

    public AppDatabase(string? databasePath = null)
    {
        DatabasePath = databasePath ?? DefaultPath();
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SQLiteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Version = 3,
            FailIfMissing = false,
            Pooling = true,
            JournalMode = SQLiteJournalModeEnum.Wal,
            SyncMode = SynchronizationModes.Normal,
            BusyTimeout = 5000,
            ForeignKeys = true
        }.ToString();
    }

    public string DatabasePath { get; }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CameraManagementSystem",
        "cms.db");

    public SQLiteConnection OpenConnection()
    {
        var connection = new SQLiteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
    }

    /// <summary>Runs a statement and returns the rowid it inserted.</summary>
    public static long ExecuteInsert(SQLiteConnection connection, SQLiteCommand command)
    {
        command.ExecuteNonQuery();
        return connection.LastInsertRowId;
    }

    private const string SchemaSql = @"
        CREATE TABLE IF NOT EXISTS cameras (
            id                  INTEGER PRIMARY KEY AUTOINCREMENT,
            channel             INTEGER NOT NULL,
            name                TEXT    NOT NULL,
            ip_address          TEXT    NOT NULL DEFAULT '',
            port                INTEGER NOT NULL DEFAULT 554,
            onvif_port          INTEGER NOT NULL DEFAULT 80,
            username            TEXT    NOT NULL DEFAULT '',
            password            TEXT    NOT NULL DEFAULT '',
            stream_url          TEXT    NOT NULL DEFAULT '',
            protocol            INTEGER NOT NULL DEFAULT 0,
            kind                INTEGER NOT NULL DEFAULT 0,
            ptz_supported       INTEGER NOT NULL DEFAULT 0,
            object_detection    INTEGER NOT NULL DEFAULT 1,
            face_recognition    INTEGER NOT NULL DEFAULT 1,
            recording_enabled   INTEGER NOT NULL DEFAULT 0,
            enabled             INTEGER NOT NULL DEFAULT 1,
            created_utc         TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS users (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            username       TEXT    NOT NULL UNIQUE COLLATE NOCASE,
            role           INTEGER NOT NULL DEFAULT 0,
            enabled        INTEGER NOT NULL DEFAULT 1,
            password_hash  TEXT    NOT NULL,
            password_salt  TEXT    NOT NULL,
            permissions    TEXT    NOT NULL DEFAULT '',
            created_utc    TEXT    NOT NULL,
            last_login_utc TEXT
        );

        CREATE TABLE IF NOT EXISTS faces (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            name           TEXT    NOT NULL,
            face_group     INTEGER NOT NULL DEFAULT 1,
            note           TEXT    NOT NULL DEFAULT '',
            registered_utc TEXT    NOT NULL,
            thumbnail      BLOB,
            embedding      BLOB    NOT NULL,
            enabled        INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS events (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp_utc TEXT    NOT NULL,
            kind          INTEGER NOT NULL,
            severity      INTEGER NOT NULL DEFAULT 0,
            camera_id     INTEGER NOT NULL DEFAULT 0,
            camera_name   TEXT    NOT NULL DEFAULT '',
            message       TEXT    NOT NULL DEFAULT '',
            score         REAL,
            snapshot      BLOB
        );

        CREATE INDEX IF NOT EXISTS idx_events_time ON events (timestamp_utc DESC);
        CREATE INDEX IF NOT EXISTS idx_events_camera ON events (camera_id, timestamp_utc DESC);

        CREATE TABLE IF NOT EXISTS recordings (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            camera_id   INTEGER NOT NULL,
            camera_name TEXT    NOT NULL DEFAULT '',
            start_utc   TEXT    NOT NULL,
            end_utc     TEXT    NOT NULL,
            file_path   TEXT    NOT NULL,
            size_bytes  INTEGER NOT NULL DEFAULT 0,
            has_events  INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_recordings_camera ON recordings (camera_id, start_utc DESC);

        CREATE TABLE IF NOT EXISTS settings (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
    ";
}
