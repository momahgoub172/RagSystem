using Microsoft.Extensions.DependencyInjection;
using RagSystem.Core.Options;

namespace RagSystem.Ingestion.Sql;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRagSystemSql(
        this IServiceCollection services,
        SqlSourceOptions options)
    {
        throw new NotImplementedException();
    }
}
