namespace RagSystem.Core.Models;

public class RetrievedChunk
{
    public required DocumentChunk Chunk { get; init; }
    public required float Score { get; init; }
}
