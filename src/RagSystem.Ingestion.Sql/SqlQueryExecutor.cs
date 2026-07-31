using Dapper;
using Microsoft.Data.SqlClient;
namespace RagSystem.Ingestion.Sql;

public class SqlQueryExecutor
{
    private readonly string _readOnlyConnectionString;

    public SqlQueryExecutor(string readOnlyConnectionString)
    {
        _readOnlyConnectionString = readOnlyConnectionString;
    }

    public async Task<IEnumerable<dynamic>> ExecuteAsync(string sql, int maxRows = 200, int timeoutSeconds = 10)
    {
        await using var conn = new SqlConnection(_readOnlyConnectionString);

        // Wrap with TOP if not already limited — crude but effective guardrail
        var cappedSql = sql.TrimStart().StartsWith("SELECT TOP", StringComparison.OrdinalIgnoreCase)
            ? sql
            : sql.Replace("SELECT", $"SELECT TOP {maxRows}", StringComparison.OrdinalIgnoreCase);

        return await conn.QueryAsync(cappedSql, commandTimeout: timeoutSeconds);
    }
}
