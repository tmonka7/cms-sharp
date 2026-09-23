using CMS.Core.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CMS.Core.Data.Mongo;

/// <summary>
/// Owns the MongoDB connection used for the face gallery and attendance.
///
/// Everything here is written so that an unreachable server is a reported
/// condition rather than an exception escaping into the interface. The rest of
/// the product has no server dependency at all, and adding one must not change
/// that for the screens which do not use it.
/// </summary>
public sealed class MongoContext : IDisposable
{
    public const string FacesCollection = "faces";
    public const string SessionsCollection = "attendance_sessions";
    public const string RecordsCollection = "attendance_records";

    private readonly object _gate = new object();
    private MongoClient? _client;
    private IMongoDatabase? _database;
    private bool _disposed;

    public MongoContext(AppSettings settings) => Configure(settings);

    /// <summary>True once a connection has been established and the schema prepared.</summary>
    public bool IsConnected { get; private set; }

    public string? LastError { get; private set; }

    public string ConnectionString { get; private set; } = string.Empty;

    public string DatabaseName { get; private set; } = string.Empty;

    public IMongoDatabase Database => _database
        ?? throw new InvalidOperationException("The MongoDB connection has not been established.");

    public IMongoCollection<BsonDocument> Faces => Database.GetCollection<BsonDocument>(FacesCollection);

    public IMongoCollection<BsonDocument> Sessions => Database.GetCollection<BsonDocument>(SessionsCollection);

    public IMongoCollection<BsonDocument> Records => Database.GetCollection<BsonDocument>(RecordsCollection);

    public void Configure(AppSettings settings)
    {
        lock (_gate)
        {
            ConnectionString = settings.MongoConnectionString;
            DatabaseName = settings.MongoDatabase;
            IsConnected = false;
            _client = null;
            _database = null;
            LastError = null;
        }
    }

    /// <summary>
    /// Connects and creates the indexes. Returns false with a reason rather than
    /// throwing, because this runs at start-up and a stopped database server
    /// must not prevent the application from opening.
    /// </summary>
    public bool Connect(TimeSpan? timeout = null)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(ConnectionString) || string.IsNullOrWhiteSpace(DatabaseName))
            {
                LastError = "No MongoDB connection string or database name is configured.";
                IsConnected = false;
                return false;
            }

            try
            {
                var settings = MongoClientSettings.FromConnectionString(ConnectionString);

                // Short timeouts: the caller is often the start-up path or a
                // button press, and a default 30 second wait on a server that is
                // not running reads as the application having hung.
                var wait = timeout ?? TimeSpan.FromSeconds(5);
                settings.ConnectTimeout = wait;
                settings.ServerSelectionTimeout = wait;
                settings.SocketTimeout = TimeSpan.FromSeconds(20);

                _client = new MongoClient(settings);
                _database = _client.GetDatabase(DatabaseName);

                // GetDatabase is lazy, so the connection is only proven by a
                // command that reaches the server.
                _database.RunCommand<BsonDocument>(new BsonDocument("ping", 1));

                EnsureIndexes();

                IsConnected = true;
                LastError = null;
                return true;
            }
            catch (Exception ex) when (
                ex is MongoException ||
                ex is TimeoutException ||
                ex is System.Security.Authentication.AuthenticationException ||
                ex is ArgumentException ||
                ex is FormatException)
            {
                LastError = ex.Message;
                IsConnected = false;
                _client = null;
                _database = null;
                return false;
            }
        }
    }

    private void EnsureIndexes()
    {
        if (_database == null)
        {
            return;
        }

        var faces = Faces;

        faces.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("enabled").Ascending("embeddingVersion")));

        faces.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("provisional")));

        faces.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("name")));

        Sessions.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("startedUtc")));

        Sessions.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("cameraId").Descending("startedUtc")));

        Records.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("sessionId")));

        Records.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("personId").Descending("seenUtc")));

        Records.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("seenUtc")));
    }

    /// <summary>Re-reads the settings and reconnects. Used by Reload on the Settings screen.</summary>
    public bool Reconnect(AppSettings settings, TimeSpan? timeout = null)
    {
        Configure(settings);
        return Connect(timeout);
    }

    public string Describe()
    {
        if (IsConnected)
        {
            return DatabaseName + " at " + Redact(ConnectionString);
        }

        return "not connected" + (string.IsNullOrEmpty(LastError) ? string.Empty : " - " + LastError);
    }

    /// <summary>Removes any credentials before a connection string is displayed or logged.</summary>
    public static string Redact(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return string.Empty;
        }

        var at = connectionString.LastIndexOf('@');
        if (at < 0)
        {
            return connectionString;
        }

        var scheme = connectionString.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0 || at <= scheme)
        {
            return connectionString;
        }

        return connectionString.Substring(0, scheme + 3) + "***@" + connectionString.Substring(at + 1);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            // The 2.x driver has no explicit client shutdown; dropping the
            // references lets the connection pool be collected.
            _client = null;
            _database = null;
            IsConnected = false;
        }
    }
}
