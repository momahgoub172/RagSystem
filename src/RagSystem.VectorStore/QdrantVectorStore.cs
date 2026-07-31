using Qdrant.Client;
using Qdrant.Client.Grpc;
using RagSystem.Core.Interfaces;
using RagSystem.Core.Models;

namespace RagSystem.VectorStore;

public class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;

    public QdrantVectorStore(QdrantClient client)
    {
        _client = client;
    }

    public async Task EnsureCollectionAsync(string collection, ulong vectorSize)
    {
        var exists = await _client.CollectionExistsAsync(collection);
        if (!exists)
        {
            await _client.CreateCollectionAsync(
                collectionName: collection,
                vectorsConfig: new VectorParams
                {
                    Size = vectorSize,
                    Distance = Distance.Cosine
                });
        }
    }

    public async Task UpsertAsync(string collection, DocumentChunk chunk, float[] embedding)
    {
        var point = new PointStruct
        {
            Id = new PointId { Uuid = ToDeterministicGuid(chunk.Id).ToString() },
            Vectors = embedding,
            Payload =
            {
                ["content"] = chunk.Content,
                ["source_file"] = chunk.SourceFile,
                ["chunk_index"] = chunk.ChunkIndex,
                ["page_number"] = chunk.PageNumber ?? -1
            }
        };

        await _client.UpsertAsync(collection, new List<PointStruct> { point });
    }

    public async Task<IEnumerable<RetrievedChunk>> SearchAsync(
        string collection, float[] queryEmbedding, int topK = 5)
    {
        var results = await _client.SearchAsync(
            collectionName: collection,
            vector: queryEmbedding,
            limit: (ulong)topK);

        return results.Select(r => new RetrievedChunk
        {
            Chunk = new DocumentChunk
            {
                Id = r.Id.Uuid,
                Content = r.Payload["content"].StringValue,
                SourceFile = r.Payload["source_file"].StringValue,
                ChunkIndex = (int)r.Payload["chunk_index"].IntegerValue,
                PageNumber = r.Payload["page_number"].IntegerValue == -1
                    ? null
                    : (int)r.Payload["page_number"].IntegerValue
            },
            Score = r.Score
        });
    }

    // Qdrant point IDs must be UUIDs or unsigned ints — we generate a
    // deterministic GUID from the chunk's string Id so re-ingesting the
    // same chunk overwrites rather than duplicates.
    private static Guid ToDeterministicGuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}
