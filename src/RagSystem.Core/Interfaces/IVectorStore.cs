namespace RagSystem.Core.Interfaces;

using RagSystem.Core.Models;

public interface IVectorStore
{
    Task EnsureCollectionAsync(string collection, ulong vectorSize);
    Task UpsertAsync(string collection, DocumentChunk chunk, float[] embedding);
    Task<IEnumerable<RetrievedChunk>> SearchAsync(string collection, float[] queryEmbedding, int topK = 5);
}
