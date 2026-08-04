using RagSystem.Core.Models;
using System.Text.Json;

namespace RagSystem.Ingestion.Sql;

public class SchemaDescriptionProvider : ISchemaDescriptionProvider
{

    private readonly Dictionary<string, TableDescriptionEntry> _descriptions;
    public SchemaDescriptionProvider(string descriptionsFilePath)
    {
        if(!File.Exists(descriptionsFilePath))
        {
            _descriptions = new Dictionary<string, TableDescriptionEntry>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(descriptionsFilePath);
        _descriptions = JsonSerializer.Deserialize<Dictionary<string, TableDescriptionEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new Dictionary<string, TableDescriptionEntry>(StringComparer.OrdinalIgnoreCase);

        // Rebuild with case-insensitive comparer regardless of what Deserialize produced
        _descriptions = new Dictionary<string, TableDescriptionEntry>(_descriptions, StringComparer.OrdinalIgnoreCase);
    }

    public List<TableSchema> Enrich(List<TableSchema> tables)
    {
        foreach (var table in tables) 
        {
            if(_descriptions.TryGetValue(table.TableName, out var descriptionEntry))
            {
                table.Description = descriptionEntry.Description ?? table.Description;
                if (descriptionEntry.Columns != null)
                {
                    foreach (var column in table.Columns)
                    {
                        if (descriptionEntry.Columns.TryGetValue(column.ColumnName, out var columnDescription))
                        {
                            column.Description = columnDescription;
                        }
                    }
                }
            }

        }
        return tables;
    }

    private class TableDescriptionEntry
    {
        public string? Description { get; set; }
        public Dictionary<string, string>? Columns { get; set; }
    }
}


