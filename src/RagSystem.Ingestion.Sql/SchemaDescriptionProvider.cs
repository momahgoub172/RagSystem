using RagSystem.Core.Models;

namespace RagSystem.Ingestion.Sql;

public class SchemaDescriptionProvider
{
    public SchemaDescriptionProvider(string descriptionsFilePath)
    {
    }

    public List<TableSchema> Enrich(List<TableSchema> tables)
    {
        throw new NotImplementedException();
    }
}
