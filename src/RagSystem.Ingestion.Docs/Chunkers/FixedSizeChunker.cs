using RagSystem.Core.Interfaces;

namespace RagSystem.Ingestion.Docs;

public class FixedSizeChunker : IChunker
{
    public IEnumerable<string> Chunk(string text, int maxTokens = 400, int overlapTokens = 50)
    {
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
            yield break;

        var step = Math.Max(1, maxTokens - overlapTokens);

        for (int start = 0; start < words.Length; start += step)
        {
            int length = Math.Min(maxTokens, words.Length - start);
            var chunkWords = words.Skip(start).Take(length); // skip make complexity O(n^2) but it's ok for small chunks
            yield return string.Join(' ', chunkWords);

            if (start + length >= words.Length)
                yield break;
        }
    }
}