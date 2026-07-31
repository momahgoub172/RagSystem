using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using RagSystem.Core.Interfaces;

namespace RagSystem.VectorStore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQdrantVectorStore(
        this IServiceCollection services, string host = "localhost", int port = 6334)
    {
        services.AddSingleton(new QdrantClient(host, port));
        services.AddSingleton<IVectorStore, QdrantVectorStore>();
        return services;
    }
}
