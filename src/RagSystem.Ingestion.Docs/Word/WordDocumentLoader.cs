// using DocumentFormat.OpenXml.Packaging;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RagSystem.Core.Interfaces;
using RagSystem.Core.Models;

namespace RagSystem.Ingestion.Docs;

public class WordDocumentLoader : IDocumentLoader
{


    private readonly IChunker _chunker;

    public WordDocumentLoader(IChunker chunker)
    {
        _chunker = chunker;
    }

    public bool CanHandle(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".doc", StringComparison.OrdinalIgnoreCase);
    }


    public async Task<IEnumerable<DocumentChunk>> LoadAsync(string filePath)
    {

        var text = ExtractText(filePath);
        if (string.IsNullOrWhiteSpace(text)) return Enumerable.Empty<DocumentChunk>();
        var chunks = _chunker.Chunk(text).Select((chunk, index) => new DocumentChunk
        {
            Id = $"{Path.GetFileNameWithoutExtension(filePath)}_chunk_{index}",
            Content = chunk,
            SourceFile = Path.GetFileName(filePath),
            PageNumber = null,
            ChunkIndex = index
        });
        return chunks;
    }


    private static string ExtractText(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return string.Empty;

        var text = new StringBuilder();
        foreach (var element in body.Elements<Paragraph>())
        {
            if (!string.IsNullOrWhiteSpace(element.InnerText))
            {
                text.AppendLine(element.InnerText.Trim());
            }
        }
        // Also pull text out of tables, since InnerText on Body skips table structure sometimes
        foreach (var table in body.Elements<Table>())
        {
            foreach (var row in table.Elements<TableRow>())
            {
                var cells = row.Elements<TableCell>()
                    .Select(c => c.InnerText.Trim())
                    .Where(t => !string.IsNullOrEmpty(t));
                text.AppendLine(string.Join(" | ", cells));
            }
        }
        return text.ToString();
    }
}