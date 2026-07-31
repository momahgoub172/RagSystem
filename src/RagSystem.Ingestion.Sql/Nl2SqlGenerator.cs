using Microsoft.Extensions.AI;

namespace RagSystem.Ingestion.Sql;

public class Nl2SqlGenerator
{
    private readonly IChatClient _chatClient;

    // Few-shot examples — tailored to YOUR schema/naming. This is the single
    // biggest accuracy lever mentioned in the plan.
    private const string FewShotExamples = """
    Q: "Show total revenue by region"
    SQL: SELECT c.Region, SUM(o.TotalAmount) AS Revenue
         FROM Orders o
         JOIN Customers c ON o.CustomerId = c.CustomerId
         GROUP BY c.Region;

    Q: "List overdue orders for Acme Corp"
    SQL: SELECT o.OrderId, o.OrderDate, o.TotalAmount, o.Status
         FROM Orders o
         JOIN Customers c ON o.CustomerId = c.CustomerId
         WHERE c.Name = 'Acme Corp' AND o.Status = 'Overdue';

    Q: "How many customers are in the West region?"
    SQL: SELECT COUNT(*) AS CustomerCount
         FROM Customers
         WHERE Region = 'West';

    Q: "Which region had the most cancelled orders last year?"
    SQL: SELECT TOP 1 c.Region, COUNT(o.OrderId) AS CancelledOrders
         FROM Orders o
         JOIN Customers c ON o.CustomerId = c.CustomerId
         WHERE o.Status = 'Cancelled' AND YEAR(o.OrderDate) = YEAR(GETDATE()) - 1
         GROUP BY c.Region
         ORDER BY CancelledOrders DESC;

    Q: "Show me the top 5 highest-value orders"
    SQL: SELECT TOP 5 OrderId, TotalAmount, OrderDate
         FROM Orders
         ORDER BY TotalAmount DESC;
    """;

    public Nl2SqlGenerator(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> GenerateSqlAsync(string question, string relevantSchemaContext)
    {
        var systemPrompt = $"""
        You generate T-SQL SELECT queries for Microsoft SQL Server (NOT MySQL, NOT PostgreSQL) based on a user's question.

        CRITICAL SYNTAX RULES — this is Microsoft SQL Server T-SQL:
        - To limit result rows, use "SELECT TOP N ..." at the start of the query. NEVER use "LIMIT" (that's MySQL/Postgres syntax and will fail).
        - To get the Nth row or a range, use "ORDER BY ... OFFSET x ROWS FETCH NEXT y ROWS ONLY", never "LIMIT x OFFSET y".
        - Use square brackets [ ] for identifiers only if needed (reserved words), otherwise plain names are fine.
        - Use GETDATE() for current date/time, not NOW() or CURRENT_TIMESTAMP.
        - Use YEAR(), MONTH(), DATEPART(), DATEADD() for date logic — standard T-SQL functions.

        General rules:
        - Only generate a single SELECT statement. Never INSERT, UPDATE, DELETE, DROP, or any DDL/DML other than SELECT.
        - Only reference tables/columns shown in the schema context below.
        - Return ONLY the raw SQL, no explanation, no markdown code fences.

        Schema context:
        {relevantSchemaContext}

        Examples:
        {FewShotExamples}
        """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, question)
        };

        var response = await _chatClient.GetResponseAsync(messages);
        return response.Text.Trim().TrimEnd(';');
    }
}
