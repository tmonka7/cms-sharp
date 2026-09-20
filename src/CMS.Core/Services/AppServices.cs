using CMS.Core.Ai;
using CMS.Core.Data;
using CMS.Core.Models;
using CMS.Core.Streaming;

namespace CMS.Core.Services;

/// <summary>
/// Composition root. One instance holds the database, the repositories, the
/// stream manager and the analytics engine for the whole application.
/// </summary>
public sealed class AppServices : IDisposable
{
    private bool _disposed;

    public AppServices(string? databasePath = null)
    {
        Database = new AppDatabase(databasePath);
        Database.Initialize();

        Cameras = new CameraRepository(Database);
        Users = new UserRepository(Database);
        Faces = new FaceRepository(Database);
        Events = new EventRepository(Database);
        Recordings = new RecordingRepository(Database);
        SettingsStore = new SettingsRepository(Database);

        Settings = SettingsStore.Load();

        Auth = new AuthService(Users);
        Auth.EnsureSeedUser();

        Streams = new StreamManager();
        FaceRecognition = new FaceRecognitionService(Faces);
        Analytics = new AnalyticsEngine(Settings, FaceRecognition, Events);
        Recording = new RecordingService(Recordings, Settings);
        Monitor = new SystemMonitor(Settings.StorageRoot);

        Analytics.Attach(Streams);
        Recording.Attach(Streams);

        // Camera drop-outs belong in the event log, not just in the UI.
        Streams.ConnectionFailed += OnStreamFailed;
        Streams.StatusChanged += OnStreamStatusChanged;
    }

    public AppDatabase Database { get; }

    public CameraRepository Cameras { get; }

    public UserRepository Users { get; }

    public FaceRepository Faces { get; }

    public EventRepository Events { get; }

    public RecordingRepository Recordings { get; }

    public SettingsRepository SettingsStore { get; }

    public AppSettings Settings { get; private set; }

    public AuthService Auth { get; }

    public StreamManager Streams { get; }

    public FaceRecognitionService FaceRecognition { get; }

    public AnalyticsEngine Analytics { get; }

    public RecordingService Recording { get; }

    public SystemMonitor Monitor { get; }

    /// <summary>Raised for every event written to the log.</summary>
    public event EventHandler<EventEntry>? EventLogged;

    /// <summary>
    /// Loads the AI models. Safe to call when the model files are absent: the
    /// rest of the product keeps working and the UI reports the model state.
    /// </summary>
    public void LoadModels() => Analytics.LoadModels(Settings);

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        SettingsStore.Save(settings);
        Analytics.ApplySettings(settings);
        Recording.ApplySettings(settings);
        Monitor.SetStorageRoot(settings.StorageRoot);
    }

    /// <summary>Records a system-level event, e.g. a login or a settings change.</summary>
    public EventEntry LogEvent(
        EventKind kind,
        string message,
        EventSeverity severity = EventSeverity.Info,
        CameraDevice? camera = null)
    {
        var entry = new EventEntry
        {
            Kind = kind,
            Severity = severity,
            Message = message,
            CameraId = camera != null ? camera.Id : 0,
            CameraName = camera != null ? camera.DisplayName : string.Empty
        };

        Events.Insert(entry);
        EventLogged?.Invoke(this, entry);
        return entry;
    }

    /// <summary>Starts every enabled camera, resolving ONVIF URLs as needed.</summary>
    public async Task StartAllCamerasAsync(CancellationToken cancellationToken = default)
    {
        foreach (var camera in Cameras.GetAll().Where(c => c.Enabled))
        {
            try
            {
                await Streams.StartAsync(camera, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                LogEvent(EventKind.CameraOffline, ex.Message, EventSeverity.Warning, camera);
            }
        }
    }

    private void OnStreamFailed(object? sender, StreamFailure failure)
        => LogEvent(EventKind.CameraOffline, failure.Message, EventSeverity.Warning, failure.Camera);

    private void OnStreamStatusChanged(object? sender, CameraStream stream)
    {
        if (stream.Camera.Status == CameraStatus.Online)
        {
            LogEvent(EventKind.CameraOnline, "Stream connected.", EventSeverity.Info, stream.Camera);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Streams.ConnectionFailed -= OnStreamFailed;
        Streams.StatusChanged -= OnStreamStatusChanged;

        Analytics.Detach(Streams);
        Recording.Detach(Streams);

        Recording.Dispose();
        Analytics.Dispose();
        FaceRecognition.Dispose();
        Streams.Dispose();
    }
}
