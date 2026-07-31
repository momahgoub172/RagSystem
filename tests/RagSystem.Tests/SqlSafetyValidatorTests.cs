namespace RagSystem.Tests;

using RagSystem.Ingestion.Sql;

public class SqlSafetyValidatorTests
{
    [Fact]
    public void Validate_ValidSelect_Passes()
    {
        var validator = CreateValidator();

        var result = validator.Validate("SELECT o.OrderId FROM Orders o");

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Validate_InsertStatement_Fails()
    {
        var result = CreateValidator().Validate("INSERT INTO Orders (OrderId) VALUES (1)");

        Assert.False(result.IsValid);
        Assert.Equal("Only SELECT statements are allowed.", result.Error);
    }

    [Fact]
    public void Validate_DisallowedTable_Fails()
    {
        var result = CreateValidator().Validate("SELECT * FROM Products");

        Assert.False(result.IsValid);
        Assert.Equal("Query references disallowed table(s): Products", result.Error);
    }

    [Fact]
    public void Validate_SelectFromCommonTableExpression_Passes()
    {
        const string sql = """
            WITH RankedOrders AS (
                SELECT c.Region, o.OrderId, o.TotalAmount,
                       ROW_NUMBER() OVER (PARTITION BY c.Region ORDER BY o.TotalAmount DESC) AS OrderRank
                FROM Orders o
                JOIN Customers c ON o.CustomerId = c.CustomerId
            )
            SELECT Region, OrderId, TotalAmount
            FROM RankedOrders
            WHERE OrderRank <= 3
            """;

        var result = CreateValidator().Validate(sql);

        Assert.True(result.IsValid, result.Error);
    }

    private static SqlSafetyValidator CreateValidator() => new(["Customers", "Orders"]);
}
