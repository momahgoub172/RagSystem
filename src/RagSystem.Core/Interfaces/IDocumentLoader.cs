namespace RagSystem.Core.Interfaces;

using RagSystem.Core.Models;

public interface IDocumentLoader
{
    bool CanHandle(string filePath);
    Task<IEnumerable<DocumentChunk>> LoadAsync(string filePath);
}
