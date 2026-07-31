namespace RagSystem.Core.Models;

public class DocumentChunk
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required string SourceFile { get; init; }
    public int? PageNumber { get; init; }
    public int ChunkIndex { get; init; }
}
