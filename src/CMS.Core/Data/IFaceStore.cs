using CMS.Core.Models;

namespace CMS.Core.Data;

/// <summary>
/// Where enrolled identities live.
///
/// The gallery moved to MongoDB when automatic attendance was added, but the
/// rest of the product stayed on the local SQLite file. This interface is what
/// lets the recognition service work against either, so the application still
/// starts and shows video when the Mongo server is unreachable - only enrolment
/// and attendance stop.
/// </summary>
public interface IFaceStore
{
    /// <summary>False when the backing store cannot be reached.</summary>
    bool IsAvailable { get; }

    /// <summary>Why the store is unavailable, or null when it is fine.</summary>
    string? LastError { get; }

    /// <summary>A short description for the System Information screen.</summary>
    string Describe();

    List<FaceRecord> GetAll();

    int Count();

    /// <summary>Stores a new identity and returns its identifier as text.</summary>
    string Insert(FaceRecord record);

    void Update(FaceRecord record);

    void Delete(FaceRecord record);
}
