namespace RagSystem.Core.Models;

public class TableSchema
{
    public required string TableName { get; init; }
    public required string Description { get; init; }         // human-written or auto-generated
    public required List<ColumnSchema> Columns { get; init; }
}

public class ColumnSchema
{
    public required string ColumnName { get; init; }
    public required string DataType { get; init; }
    public string? Description { get; init; }
    public string? SampleValue { get; init; }
}
