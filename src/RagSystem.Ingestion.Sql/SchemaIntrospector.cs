using Dapper;
using Microsoft.Data.SqlClient;
using RagSystem.Core.Models;

namespace RagSystem.Ingestion.Sql;

public class SchemaIntrospector
{
    private readonly string _connectionString;

    public SchemaIntrospector(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<TableSchema>> GetSchemaAsync(IEnumerable<string> allowedTables)
    {
        await using var connection = new SqlConnection(_connectionString);

         const string columnsQuery = """
            SELECT
                t.TABLE_NAME AS TableName,
                c.COLUMN_NAME AS ColumnName,
                c.DATA_TYPE AS DataType
            FROM INFORMATION_SCHEMA.TABLES t
            JOIN INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME
            WHERE t.TABLE_TYPE = 'BASE TABLE'
              AND t.TABLE_NAME IN @AllowedTables
            ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION
            """;


            var rows = await connection.QueryAsync<(string TableName, string ColumnName, string DataType)>(columnsQuery, new { AllowedTables = allowedTables });
            var tables = rows
            .GroupBy(r => r.TableName)
            .Select(g => new TableSchema
            {
                TableName = g.Key,
                Description = "", // fill in manually below for now — see note
                Columns = g.Select(c => new ColumnSchema
                {
                    ColumnName = c.ColumnName,
                    DataType = c.DataType
                }).ToList()
            })
            .ToList();

        return tables;
    }
}
