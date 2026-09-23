using CMS.Core.Ai;

namespace CMS.Core.Attendance;

/// <summary>
/// Groups the captures from one sweep into people.
///
/// This step is what makes the feature usable. A sweep sees the same person at
/// several stops, because consecutive tiles overlap deliberately, and several
/// times within each stop. Writing the captures straight to the database would
/// register one person a dozen times over, and every one of those records would
/// then compete to match them on the next sweep.
/// </summary>
public static class FaceClusterer
{
    /// <summary>
    /// Agglomerative clustering with a single pass of merging, using average
    /// linkage against the running centroid.
    ///
    /// Average linkage rather than nearest-neighbour: a chain of marginally
    /// similar captures would otherwise link two genuinely different people
    /// through a blurred frame that sits between them.
    /// </summary>
    public static List<FaceCluster> Cluster(IEnumerable<FaceCapture> captures, float cosineThreshold)
    {
        var clusters = new List<FaceCluster>();

        // Strongest captures first, so each cluster forms around a good example
        // rather than around whichever frame happened to arrive first.
        var ordered = captures
            .Where(c => c.Embedding.Length > 0)
            .OrderByDescending(c => c.Quality.Score)
            .ToList();

        foreach (var capture in ordered)
        {
            FaceCluster? best = null;
            var bestCosine = float.MinValue;

            foreach (var cluster in clusters)
            {
                var cosine = Cosine(capture.Embedding, cluster.Centroid);
                if (cosine > bestCosine)
                {
                    bestCosine = cosine;
                    best = cluster;
                }
            }

            if (best != null && bestCosine >= cosineThreshold)
            {
                best.Captures.Add(capture);
                best.Centroid = Recentre(best);
            }
            else
            {
                var created = new FaceCluster();
                created.Captures.Add(capture);
                created.Centroid = FaceEmbedder.Normalise((float[])capture.Embedding.Clone());
                clusters.Add(created);
            }
        }

        foreach (var cluster in clusters)
        {
            cluster.Best = cluster.Captures
                .OrderByDescending(c => c.Quality.Score)
                .First();
        }

        return clusters
            .OrderByDescending(c => c.Captures.Count)
            .ThenByDescending(c => c.Best?.Quality.Score ?? 0f)
            .ToList();
    }

    /// <summary>
    /// Mean of the member embeddings, renormalised to unit length. Averaging
    /// several views of one person is more stable than any single capture,
    /// which is why the centroid rather than the best capture is what later
    /// comparisons are made against.
    /// </summary>
    private static float[] Recentre(FaceCluster cluster)
    {
        var length = cluster.Captures[0].Embedding.Length;
        var sum = new float[length];

        foreach (var capture in cluster.Captures)
        {
            if (capture.Embedding.Length != length)
            {
                continue;
            }

            for (var i = 0; i < length; i++)
            {
                sum[i] += capture.Embedding[i];
            }
        }

        return FaceEmbedder.Normalise(sum);
    }

    /// <summary>
    /// Raw cosine, not the remapped 0 to 1 scale the interface shows. Clustering
    /// thresholds are quoted against published figures for the recognition
    /// models, which are all raw cosine.
    /// </summary>
    public static float Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return -1f;
        }

        var dot = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return MathEx.Clamp(dot, -1f, 1f);
    }
}
