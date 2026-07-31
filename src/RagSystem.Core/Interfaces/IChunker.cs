namespace RagSystem.Core.Interfaces;

public interface IChunker
{
    IEnumerable<string> Chunk(string text, int maxTokens = 400, int overlapTokens = 50);
}
