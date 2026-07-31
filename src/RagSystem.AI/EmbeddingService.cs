using Microsoft.Extensions.AI;
using RagSystem.Core.Models;

namespace RagSystem.AI;

public class EmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task<float[]> EmbedTextAsync(string text)
    {
        var result = await _embeddingGenerator.GenerateAsync(text);
        return result.Vector.ToArray();
    }

    public async Task<Dictionary<DocumentChunk, float[]>> EmbedChunksAsync(
        IEnumerable<DocumentChunk> chunks)
    {
        var chunkList = chunks.ToList();
        var texts = chunkList.Select(c => c.Content);

        var embeddings = await _embeddingGenerator.GenerateAsync(texts);

        var result = new Dictionary<DocumentChunk, float[]>();
        for (int i = 0; i < chunkList.Count; i++)
        {
            result[chunkList[i]] = embeddings[i].Vector.ToArray();
        }
        return result;
    }
}
