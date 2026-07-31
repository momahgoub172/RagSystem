using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using System.ClientModel;

namespace RagSystem.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRagSystemAi(
        this IServiceCollection services,
        string openRouterApiKey,
        string chatModel = "openai/gpt-4o-mini",
        string embeddingModel = "openai/text-embedding-3-small")
    {
        var openRouterClient = new OpenAIClient(
            new ApiKeyCredential(openRouterApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri("https://openrouter.ai/api/v1")
            });

        services.AddSingleton<IChatClient>(
            openRouterClient.GetChatClient(chatModel).AsIChatClient());

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            openRouterClient.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator());

        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<AnswerService>();

        return services;
    }
}
