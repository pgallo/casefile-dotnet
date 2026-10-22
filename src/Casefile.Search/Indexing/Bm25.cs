namespace Casefile.Search.Indexing;

/// <summary>Okapi BM25 with the non-negative IDF variant: ln(1 + (N - n + 0.5) / (n + 0.5)).</summary>
public sealed record Bm25(double K1 = 1.2, double B = 0.75)
{
    public static double Idf(int docCount, int docFreq) =>
        Math.Log(1.0 + (docCount - docFreq + 0.5) / (docFreq + 0.5));

    public double TermScore(double idf, int termFreq, int fieldLength, double avgFieldLength)
    {
        if (termFreq == 0) return 0;
        var norm = 1 - B + B * (avgFieldLength > 0 ? fieldLength / avgFieldLength : 1);
        return idf * (termFreq * (K1 + 1)) / (termFreq + K1 * norm);
    }
}
