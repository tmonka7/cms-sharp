using CMS.Core.Attendance;
using CMS.Core.Models;
using CMS.Core.Onvif;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CMS.Core.Data.Mongo;

/// <summary>
/// Attendance sessions and the per-person records they produce.
///
/// A session and its records are stored separately so that a long history can
/// be listed without loading every thumbnail, and so that a person's attendance
/// can be queried directly rather than by scanning sessions.
/// </summary>
public sealed class MongoAttendanceRepository
{
    private readonly MongoContext _context;

    public MongoAttendanceRepository(MongoContext context) => _context = context;

    public bool IsAvailable => _context.IsConnected;

    public string? LastError => _context.LastError;

    /// <summary>Writes a session and all of its entries. Returns the session identifier.</summary>
    public string Save(AttendanceSession session)
    {
        RequireAvailable();

        var document = new BsonDocument
        {
            { "cameraId", session.CameraId },
            { "cameraName", session.CameraName ?? string.Empty },
            { "startedUtc", session.StartedUtc },
            { "startedBy", session.StartedBy ?? string.Empty },
            { "status", (int)session.Status },
            { "statusText", session.StatusText },
            { "arcDegrees", session.ArcDegrees },
            { "stopCount", session.StopCount },
            { "facesAccepted", session.FacesAccepted },
            { "facesRejected", session.FacesRejected },
            { "peopleFound", session.PeopleFound },
            { "newlyEnrolled", session.NewlyEnrolled },
            { "embeddingVersion", session.EmbeddingVersion ?? FaceEmbeddingVersions.Current }
        };

        if (session.FinishedUtc.HasValue)
        {
            document.Add("finishedUtc", session.FinishedUtc.Value);
        }

        if (!string.IsNullOrEmpty(session.Error))
        {
            document.Add("error", session.Error);
        }

        if (ObjectId.TryParse(session.Id, out var existing))
        {
            _context.Sessions.ReplaceOne(
                Builders<BsonDocument>.Filter.Eq("_id", existing),
                document,
                new ReplaceOptions { IsUpsert = true });
        }
        else
        {
            _context.Sessions.InsertOne(document);
            session.Id = document["_id"].AsObjectId.ToString();
        }

        SaveEntries(session);
        return session.Id;
    }

    private void SaveEntries(AttendanceSession session)
    {
        if (session.Entries.Count == 0)
        {
            return;
        }

        // Replacing rather than appending, so re-saving a session after a
        // correction does not duplicate the people it found.
        _context.Records.DeleteMany(Builders<BsonDocument>.Filter.Eq("sessionId", session.Id));

        var documents = new List<BsonDocument>(session.Entries.Count);

        foreach (var entry in session.Entries)
        {
            var document = new BsonDocument
            {
                { "sessionId", session.Id },
                { "cameraId", session.CameraId },
                { "cameraName", session.CameraName ?? string.Empty },
                { "personId", entry.PersonId ?? string.Empty },
                { "name", entry.Name ?? string.Empty },
                { "faceGroup", (int)entry.Group },
                { "outcome", (int)entry.Outcome },
                { "outcomeText", entry.OutcomeText },
                { "similarity", entry.Similarity },
                { "captureScore", entry.CaptureScore },
                { "captureCount", entry.CaptureCount },
                { "stopSpan", entry.StopSpan },
                { "seenUtc", entry.SeenUtc },
                { "needsReview", entry.NeedsReview },
                { "reviewReason", entry.ReviewReason ?? string.Empty },
                { "pan", entry.Position.Pan },
                { "tilt", entry.Position.Tilt },
                { "zoom", entry.Position.Zoom }
            };

            if (entry.Thumbnail != null && entry.Thumbnail.Length > 0)
            {
                document.Add("thumbnail", new BsonBinaryData(entry.Thumbnail));
            }

            documents.Add(document);
        }

        _context.Records.InsertMany(documents);
    }

    /// <summary>Recent sessions, newest first, without their entries.</summary>
    public List<AttendanceSession> GetSessions(int limit = 50, int? cameraId = null)
    {
        if (!IsAvailable)
        {
            return new List<AttendanceSession>();
        }

        var filter = cameraId.HasValue
            ? Builders<BsonDocument>.Filter.Eq("cameraId", cameraId.Value)
            : Builders<BsonDocument>.Filter.Empty;

        return _context.Sessions
            .Find(filter)
            .SortByDescending(d => d["startedUtc"])
            .Limit(limit)
            .ToList()
            .Select(MapSession)
            .ToList();
    }

    /// <summary>The people found by one session.</summary>
    public List<AttendanceEntry> GetEntries(string sessionId)
    {
        if (!IsAvailable || string.IsNullOrEmpty(sessionId))
        {
            return new List<AttendanceEntry>();
        }

        return _context.Records
            .Find(Builders<BsonDocument>.Filter.Eq("sessionId", sessionId))
            .SortByDescending(d => d["seenUtc"])
            .ToList()
            .Select(MapEntry)
            .ToList();
    }

    /// <summary>Every sighting of one person, newest first.</summary>
    public List<AttendanceEntry> GetForPerson(string personId, int limit = 200)
    {
        if (!IsAvailable || string.IsNullOrEmpty(personId))
        {
            return new List<AttendanceEntry>();
        }

        return _context.Records
            .Find(Builders<BsonDocument>.Filter.Eq("personId", personId))
            .SortByDescending(d => d["seenUtc"])
            .Limit(limit)
            .ToList()
            .Select(MapEntry)
            .ToList();
    }

    /// <summary>Attendance over a date range, for a daily or weekly report.</summary>
    public List<AttendanceEntry> GetBetween(DateTime fromUtc, DateTime toUtc, int limit = 2000)
    {
        if (!IsAvailable)
        {
            return new List<AttendanceEntry>();
        }

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Gte("seenUtc", fromUtc),
            Builders<BsonDocument>.Filter.Lt("seenUtc", toUtc));

        return _context.Records
            .Find(filter)
            .SortByDescending(d => d["seenUtc"])
            .Limit(limit)
            .ToList()
            .Select(MapEntry)
            .ToList();
    }

    public void DeleteSession(string sessionId)
    {
        RequireAvailable();

        if (!ObjectId.TryParse(sessionId, out var id))
        {
            return;
        }

        _context.Records.DeleteMany(Builders<BsonDocument>.Filter.Eq("sessionId", sessionId));
        _context.Sessions.DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", id));
    }

    private void RequireAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "The attendance database is not available: " + (_context.LastError ?? "not connected"));
        }
    }

    private static AttendanceSession MapSession(BsonDocument document) => new AttendanceSession
    {
        Id = document.Contains("_id") ? document["_id"].AsObjectId.ToString() : string.Empty,
        CameraId = ReadInt(document, "cameraId"),
        CameraName = ReadString(document, "cameraName"),
        StartedUtc = ReadDate(document, "startedUtc") ?? DateTime.UtcNow,
        FinishedUtc = ReadDate(document, "finishedUtc"),
        StartedBy = ReadString(document, "startedBy"),
        Status = (SweepStatus)ReadInt(document, "status"),
        ArcDegrees = ReadFloat(document, "arcDegrees"),
        StopCount = ReadInt(document, "stopCount"),
        FacesAccepted = ReadInt(document, "facesAccepted"),
        FacesRejected = ReadInt(document, "facesRejected"),
        PeopleFound = ReadInt(document, "peopleFound"),
        NewlyEnrolled = ReadInt(document, "newlyEnrolled"),
        EmbeddingVersion = ReadString(document, "embeddingVersion"),
        Error = document.Contains("error") ? ReadString(document, "error") : null
    };

    private static AttendanceEntry MapEntry(BsonDocument document)
    {
        var entry = new AttendanceEntry
        {
            PersonId = ReadString(document, "personId"),
            Name = ReadString(document, "name"),
            Group = (FaceGroup)ReadInt(document, "faceGroup"),
            Outcome = (AttendanceOutcome)ReadInt(document, "outcome"),
            Similarity = ReadFloat(document, "similarity"),
            CaptureScore = ReadFloat(document, "captureScore"),
            CaptureCount = ReadInt(document, "captureCount"),
            StopSpan = ReadInt(document, "stopSpan"),
            SeenUtc = ReadDate(document, "seenUtc") ?? DateTime.UtcNow,
            NeedsReview = document.Contains("needsReview") && document["needsReview"].AsBoolean,
            ReviewReason = ReadString(document, "reviewReason"),
            Position = new PtzPosition(
                ReadFloat(document, "pan"),
                ReadFloat(document, "tilt"),
                ReadFloat(document, "zoom"))
        };

        if (document.Contains("thumbnail") && document["thumbnail"].IsBsonBinaryData)
        {
            entry.Thumbnail = document["thumbnail"].AsBsonBinaryData.Bytes;
        }

        return entry;
    }

    private static string ReadString(BsonDocument document, string name)
        => document.Contains(name) && !document[name].IsBsonNull ? document[name].AsString : string.Empty;

    private static int ReadInt(BsonDocument document, string name, int fallback = 0)
        => document.Contains(name) && document[name].IsNumeric ? document[name].ToInt32() : fallback;

    private static float ReadFloat(BsonDocument document, string name)
        => document.Contains(name) && document[name].IsNumeric ? (float)document[name].ToDouble() : 0f;

    private static DateTime? ReadDate(BsonDocument document, string name)
    {
        if (!document.Contains(name) || document[name].IsBsonNull || !document[name].IsValidDateTime)
        {
            return null;
        }

        return DateTime.SpecifyKind(document[name].ToUniversalTime(), DateTimeKind.Utc);
    }
}
