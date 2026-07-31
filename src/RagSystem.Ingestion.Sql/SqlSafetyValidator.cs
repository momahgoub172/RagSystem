using Microsoft.SqlServer.TransactSql.ScriptDom;
using RagSystem.Core.Models;

namespace RagSystem.Ingestion.Sql;

public class SqlValidationResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
}


public class SqlSafetyValidator
{
    private readonly HashSet<string> _allowedTables;

    public SqlSafetyValidator(IEnumerable<string> allowedTables)
    {
        _allowedTables = new HashSet<string>(allowedTables, StringComparer.OrdinalIgnoreCase);
    }

    public SqlValidationResult Validate(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);

        if (errors.Count > 0)
            return new SqlValidationResult { IsValid = false, Error = "SQL failed to parse: " + errors[0].Message };

        // Must be exactly one statement, and it must be a SELECT
        if (fragment is not TSqlScript script || script.Batches.Count != 1 ||
            script.Batches[0].Statements.Count != 1)
        {
            return new SqlValidationResult { IsValid = false, Error = "Only a single statement is allowed." };
        }

        if (script.Batches[0].Statements[0] is not SelectStatement)
        {
            return new SqlValidationResult { IsValid = false, Error = "Only SELECT statements are allowed." };
        }



        // Walk the tree to find referenced table names and check against allow-list
        var tableVisitor = new TableNameVisitor();
        fragment.Accept(tableVisitor);

        var disallowed = tableVisitor.TableNames
            .Where(t => !_allowedTables.Contains(t))
            .ToList();

        if (disallowed.Any())
        {
            return new SqlValidationResult
            {
                IsValid = false,
                Error = $"Query references disallowed table(s): {string.Join(", ", disallowed)}"
            };
        }

        return new SqlValidationResult { IsValid = true };
    }

    private class TableNameVisitor : TSqlFragmentVisitor
    {
        public List<string> TableNames { get; } = new();

        public override void Visit(NamedTableReference node)
        {
            TableNames.Add(node.SchemaObject.BaseIdentifier.Value);
            base.Visit(node);
        }
    }
}