using CMS.Core.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CMS.Core.Data.Mongo;

/// <summary>
/// The enrolled gallery, held in MongoDB.
///
/// Documents are handled as BsonDocument rather than through a mapped class, so
/// that a document written by a later version carrying fields this build does
/// not know about is read and rewritten without losing them.
/// </summary>
public sealed class MongoFaceRepository : IFaceStore
{
    private readonly MongoContext _context;

    public MongoFaceRepository(MongoContext context) => _context = context;

    public bool IsAvailable => _context.IsConnected;

    public string? LastError => _context.LastError;

    public string Describe() => "MongoDB " + _context.Describe();

    public List<FaceRecord> GetAll()
    {
        if (!IsAvailable)
        {
            return new List<FaceRecord>();
        }

        var documents = _context.Faces
            .Find(Builders<BsonDocument>.Filter.Empty)
            .SortBy(d => d["name"])
            .ToList();

        return documents.Select(Map).ToList();
    }

    public int Count()
        => IsAvailable ? (int)_context.Faces.CountDocuments(Builders<BsonDocument>.Filter.Empty) : 0;

    public string Insert(FaceRecord record)
    {
        RequireAvailable();

        var document = ToDocument(record, includeId: false);
        _context.Faces.InsertOne(document);

        record.DocumentId = document["_id"].AsObjectId.ToString();
        return record.DocumentId;
    }

    public void Update(FaceRecord record)
    {
        RequireAvailable();

        if (!ObjectId.TryParse(record.DocumentId, out var id))
        {
            throw new InvalidOperationException("This face record has no MongoDB identifier.");
        }

        var document = ToDocument(record, includeId: false);

        _context.Faces.ReplaceOne(
            Builders<BsonDocument>.Filter.Eq("_id", id),
            document);
    }

    public void Delete(FaceRecord record)
    {
        RequireAvailable();

        if (!ObjectId.TryParse(record.DocumentId, out var id))
        {
            return;
        }

        _context.Faces.DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", id));
    }

    /// <summary>
    /// Records a further sighting of someone already enrolled, without replacing
    /// the stored embedding. A sweep should raise confidence in an identity, not
    /// overwrite a good enrolment with whatever today's capture happened to be.
    /// </summary>
    public void RecordSighting(FaceRecord record, DateTime seenUtc)
    {
        if (!IsAvailable || !ObjectId.TryParse(record.DocumentId, out var id))
        {
            return;
        }

        _context.Faces.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", id),
            Builders<BsonDocument>.Update
                .Set("lastSeenUtc", seenUtc)
                .Inc("captureCount", 1));

        record.LastSeenUtc = seenUtc;
        record.CaptureCount++;
    }

    /// <summary>Identities created by a sweep that nobody has named yet.</summary>
    public List<FaceRecord> GetProvisional()
    {
        if (!IsAvailable)
        {
            return new List<FaceRecord>();
        }

        return _context.Faces
            .Find(Builders<BsonDocument>.Filter.Eq("provisional", true))
            .SortByDescending(d => d["registeredUtc"])
            .ToList()
            .Select(Map)
            .ToList();
    }

    /// <summary>
    /// Names a provisional identity, turning it into an ordinary enrolment.
    /// </summary>
    public void Confirm(FaceRecord record, string name, FaceGroup group)
    {
        RequireAvailable();

        if (!ObjectId.TryParse(record.DocumentId, out var id))
        {
            throw new InvalidOperationException("This face record has no MongoDB identifier.");
        }

        _context.Faces.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", id),
            Builders<BsonDocument>.Update
                .Set("name", name)
                .Set("faceGroup", (int)group)
                .Set("provisional", false));

        record.Name = name;
        record.Group = group;
        record.Provisional = false;
    }

    /// <summary>
    /// Merges one identity into another, keeping the target. Used when a sweep
    /// created a provisional identity for somebody already enrolled.
    /// </summary>
    public void Merge(FaceRecord source, FaceRecord target)
    {
        RequireAvailable();

        if (!ObjectId.TryParse(source.DocumentId, out var sourceId) ||
            !ObjectId.TryParse(target.DocumentId, out var targetId))
        {
            throw new InvalidOperationException("Both records must come from MongoDB to be merged.");
        }

        _context.Faces.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", targetId),
            Builders<BsonDocument>.Update.Inc("captureCount", Math.Max(1, source.CaptureCount)));

        // Attendance already written against the provisional identity is
        // repointed, so history survives the merge.
        _context.Records.UpdateMany(
            Builders<BsonDocument>.Filter.Eq("personId", source.DocumentId),
            Builders<BsonDocument>.Update.Set("personId", target.DocumentId));

        _context.Faces.DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", sourceId));
    }

    private void RequireAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "The face database is not available: " + (_context.LastError ?? "not connected"));
        }
    }

    private static BsonDocument ToDocument(FaceRecord record, bool includeId)
    {
        var document = new BsonDocument
        {
            { "name", record.Name ?? string.Empty },
            { "faceGroup", (int)record.Group },
            { "note", record.Note ?? string.Empty },
            { "registeredUtc", record.RegisteredUtc },
            { "enabled", record.Enabled },
            { "provisional", record.Provisional },
            { "embeddingVersion", record.EmbeddingVersion ?? FaceEmbeddingVersions.Legacy },
            { "captureCount", record.CaptureCount },
            { "embedding", new BsonBinaryData(ToBytes(record.Embedding)) },
            { "embeddingLength", record.Embedding.Length }
        };

        // Stored as raw float32 bytes rather than an array of doubles: the
        // vector is never queried field by field, and an array would be eight
        // times the size for no benefit.
        if (record.Thumbnail != null && record.Thumbnail.Length > 0)
        {
            document.Add("thumbnail", new BsonBinaryData(record.Thumbnail));
        }

        if (record.LastSeenUtc.HasValue)
        {
            document.Add("lastSeenUtc", record.LastSeenUtc.Value);
        }

        if (includeId && ObjectId.TryParse(record.DocumentId, out var id))
        {
            document.Add("_id", id);
        }

        return document;
    }

    private static FaceRecord Map(BsonDocument document)
    {
        var record = new FaceRecord
        {
            DocumentId = document.Contains("_id") ? document["_id"].AsObjectId.ToString() : string.Empty,
            Name = ReadString(document, "name"),
            Group = (FaceGroup)ReadInt(document, "faceGroup", (int)FaceGroup.Unknown),
            Note = ReadString(document, "note"),
            RegisteredUtc = ReadDate(document, "registeredUtc") ?? DateTime.UtcNow,
            Enabled = ReadBool(document, "enabled", true),
            Provisional = ReadBool(document, "provisional", false),
            EmbeddingVersion = ReadString(document, "embeddingVersion"),
            CaptureCount = ReadInt(document, "captureCount", 0),
            LastSeenUtc = ReadDate(document, "lastSeenUtc")
        };

        if (string.IsNullOrEmpty(record.EmbeddingVersion))
        {
            record.EmbeddingVersion = FaceEmbeddingVersions.Legacy;
        }

        if (document.Contains("embedding") && document["embedding"].IsBsonBinaryData)
        {
            record.Embedding = FromBytes(document["embedding"].AsBsonBinaryData.Bytes);
        }

        if (document.Contains("thumbnail") && document["thumbnail"].IsBsonBinaryData)
        {
            record.Thumbnail = document["thumbnail"].AsBsonBinaryData.Bytes;
        }

        return record;
    }

    private static byte[] ToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));
        return values;
    }

    private static string ReadString(BsonDocument document, string name)
        => document.Contains(name) && !document[name].IsBsonNull ? document[name].AsString : string.Empty;

    private static int ReadInt(BsonDocument document, string name, int fallback)
        => document.Contains(name) && document[name].IsNumeric ? document[name].ToInt32() : fallback;

    private static bool ReadBool(BsonDocument document, string name, bool fallback)
        => document.Contains(name) && document[name].IsBoolean ? document[name].AsBoolean : fallback;

    private static DateTime? ReadDate(BsonDocument document, string name)
    {
        if (!document.Contains(name) || document[name].IsBsonNull)
        {
            return null;
        }

        return document[name].IsValidDateTime
            ? DateTime.SpecifyKind(document[name].ToUniversalTime(), DateTimeKind.Utc)
            : (DateTime?)null;
    }
}
