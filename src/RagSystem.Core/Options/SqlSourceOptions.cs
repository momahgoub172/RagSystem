namespace RagSystem.Core.Options;

public class SqlSourceOptions
{
    public string IntrospectionConnectionString { get; set; } = string.Empty;
    public string ReadOnlyConnectionString { get; set; } = string.Empty;
    public List<string> AllowedTables { get; set; } = [];
    public string SchemaDescriptionsFile { get; set; } = "schema-descriptions.json";
}
