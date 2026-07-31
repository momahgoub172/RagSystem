using RagSystem.Core.Models;

namespace RagSystem.Ingestion.Sql;

public class SchemaCatalogBuilder
{
    public IEnumerable<(string TableName, string Text)> BuildCatalogEntries(List<TableSchema> tables)
    {

        foreach (var table in tables){
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Table: {table.TableName}");
            sb.AppendLine($"Description: {table.Description}");
            sb.AppendLine($"Columns:");
            foreach (var column in table.Columns){
                sb.AppendLine($"  - {column.ColumnName}: {column.DataType}");
            }
            yield return (table.TableName, sb.ToString());
        }   
    }
}
