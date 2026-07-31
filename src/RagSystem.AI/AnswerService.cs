using Microsoft.Extensions.AI;
using RagSystem.Core.Models;
using System.Text;

namespace RagSystem.AI;

public class AnswerService
{
    private readonly IChatClient _chatClient;

    public AnswerService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> AnswerAsync(string question, IEnumerable<RetrievedChunk> context)
    {
        var contextText = new StringBuilder();
        foreach (var item in context)
        {
            contextText.AppendLine($"[Source: {item.Chunk.SourceFile}, chunk {item.Chunk.ChunkIndex}]");
            contextText.AppendLine(item.Chunk.Content);
            contextText.AppendLine();
        }

        var systemPrompt = """
            You are a helpful assistant answering questions based only on the provided context.
            If the context doesn't contain enough information to answer, say so clearly.
            Always cite which source file(s) you used in your answer.
            """;

        var userPrompt = $"""
            Context:
            {contextText}

            Question: {question}
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var response = await _chatClient.GetResponseAsync(messages);
        return response.Text ?? string.Empty;
    }

    public async Task<string> AnswerRawAsync(string prompt)
{
    var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
    var response = await _chatClient.GetResponseAsync(messages);
    return response.Text;
}
}
