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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);

        await using var conn = new SqlConnection(_readOnlyConnectionString);
        await conn.OpenAsync();

        // SET ROWCOUNT caps this connection's result set without rewriting generated
        // SQL. Text replacement breaks valid constructs such as SELECT DISTINCT and
        // can accidentally modify subqueries.
        await conn.ExecuteAsync($"SET ROWCOUNT {maxRows}", commandTimeout: timeoutSeconds);

        try
        {
            return await conn.QueryAsync(sql, commandTimeout: timeoutSeconds);
        }
        finally
        {
            await conn.ExecuteAsync("SET ROWCOUNT 0", commandTimeout: timeoutSeconds);
        }
    }
}
