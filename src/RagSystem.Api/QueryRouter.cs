// RagSystem.Api/QueryRouter.cs
using Microsoft.Extensions.AI;

namespace RagSystem.Api;

public enum QueryIntent { Document, Database }

public class QueryRouter
{
    private readonly IChatClient _chatClient;

    public QueryRouter(IChatClient chatClient) => _chatClient = chatClient;

    public async Task<QueryIntent> ClassifyAsync(string question)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                "Classify the user's question as exactly one word: DOCUMENT or DATABASE. " +
                "DOCUMENT = questions about policies, manuals, general text content. " +
                "DATABASE = questions about sales, orders, customers, inventory, counts, totals, or specific records. " +
                "Reply with only the single word."),
            new(ChatRole.User, question)
        };

        var response = await _chatClient.GetResponseAsync(messages);
        var answer = response.Text.Trim().ToUpperInvariant();

        return answer.Contains("DATABASE") ? QueryIntent.Database : QueryIntent.Document;
    }
}