// ============================================================
// THROWAWAY PROTOTYPE — Task 11.3a. Synthetic embedding strategy.
// Deterministic, local, ZERO provider calls. Thousands of vectors.
//
// Approach: each topic owns a fixed random centroid in R^dim. A
// document's embedding = topic centroid + small deterministic jitter
// derived from a hash of the text, then L2-normalized. This gives a
// real (crude) semantic signal — same-topic docs cluster, cross-topic
// docs separate — which is enough to exercise pgvector index behavior
// and centroid math. It CANNOT prove real semantic recall quality.
// ============================================================

using System.Security.Cryptography;
using System.Text;

namespace VectorPrototype;

public sealed class SyntheticEmbedder
{
    private readonly float[][] _topicCentroids;
    private readonly int _dimensions;
    private readonly float _jitterScale;

    public SyntheticEmbedder(int seed, int dimensions, float jitterScale = 0.15f)
    {
        _dimensions = dimensions;
        _jitterScale = jitterScale;
        var rng = new Random(seed);
        _topicCentroids = new float[CorpusGenerator.TopicNames.Length][];
        for (int t = 0; t < _topicCentroids.Length; t++)
        {
            var v = new float[dimensions];
            for (int i = 0; i < dimensions; i++)
                v[i] = (float)(rng.NextDouble() * 2 - 1);
            _topicCentroids[t] = Normalize(v);
        }
    }

    /// <summary>Deterministic jitter from text content so identical text → identical vector.</summary>
    private float[] TextJitter(string text)
    {
        var v = new float[_dimensions];
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        // Expand the 32-byte hash across all dimensions deterministically.
        for (int i = 0; i < _dimensions; i++)
        {
            byte b = hash[i % hash.Length];
            v[i] = ((b / 255f) * 2f - 1f) * _jitterScale;
        }
        return v;
    }

    /// <summary>Embed a document as (topic centroid + text jitter), L2-normalized.</summary>
    public float[] Embed(int topicIndex, string title, string body)
    {
        var centroid = _topicCentroids[topicIndex];
        var jitter = TextJitter(title + "|" + body);
        var v = new float[_dimensions];
        for (int i = 0; i < _dimensions; i++)
            v[i] = centroid[i] + jitter[i];
        return Normalize(v);
    }

    public static float[] Normalize(float[] v)
    {
        double sumSq = 0;
        foreach (var x in v) sumSq += x * x;
        var norm = (float)Math.Sqrt(sumSq);
        if (norm == 0) return v;
        var result = new float[v.Length];
        for (int i = 0; i < v.Length; i++) result[i] = v[i] / norm;
        return result;
    }

    /// <summary>Weighted centroid of normalized child vectors, then re-normalized (DATA_MODEL §3.23).</summary>
    public static float[] WeightedCentroid(IReadOnlyList<(float[] Vector, double Weight)> components)
    {
        int dim = components[0].Vector.Length;
        var acc = new double[dim];
        foreach (var (vec, weight) in components)
            for (int i = 0; i < dim; i++)
                acc[i] += vec[i] * weight;
        var result = new float[dim];
        for (int i = 0; i < dim; i++) result[i] = (float)acc[i];
        return Normalize(result);
    }

    public static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0;
        for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot; // both already normalized
    }
}
