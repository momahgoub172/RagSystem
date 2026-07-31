using RagSystem.AI;
using RagSystem.Core.Interfaces;
using RagSystem.Ingestion.Docs;
using RagSystem.VectorStore;

var builder = WebApplication.CreateBuilder(args);

// --- Services ---
builder.Services.AddQdrantVectorStore(host: "localhost", port: 6334);

var openRouterApiKey = builder.Configuration["OpenRouter:ApiKey"];
if (string.IsNullOrWhiteSpace(openRouterApiKey))
    throw new InvalidOperationException(
        "OpenRouter:ApiKey is missing. Set it with: dotnet user-secrets set \"OpenRouter:ApiKey\" \"...\"");

builder.Services.AddRagSystemAi(
    openRouterApiKey: openRouterApiKey,
    chatModel: "openai/gpt-4o-mini",
    embeddingModel: "openai/text-embedding-3-small");

builder.Services.AddSingleton<IChunker, FixedSizeChunker>();
builder.Services.AddSingleton<IDocumentLoader, WordDocumentLoader>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string CollectionName = "docs";
const ulong VectorSize = 1536; // must match embeddingModel dimension

var app = builder.Build();

// Ensure the Qdrant collection exists at startup
using (var scope = app.Services.CreateScope())
{
    var vectorStore = scope.ServiceProvider.GetRequiredService<IVectorStore>();
    await vectorStore.EnsureCollectionAsync(CollectionName, VectorSize);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --- Endpoints ---

app.MapPost("/ingest/documents", async (
    IFormFile file,
    IDocumentLoader loader,
    EmbeddingService embeddingService,
    IVectorStore vectorStore) =>
{
    if (!loader.CanHandle(file.FileName))
        return Results.BadRequest($"Unsupported file type: {file.FileName}");

    // Save to a temp path since loaders work off disk paths
    var tempPath = Path.Combine(Path.GetTempPath(), file.FileName);
    await using (var stream = File.Create(tempPath))
    {
        await file.CopyToAsync(stream);
    }

    try
    {
        var chunks = await loader.LoadAsync(tempPath);
        var embedded = await embeddingService.EmbedChunksAsync(chunks);

        foreach (var (chunk, vector) in embedded)
        {
            await vectorStore.UpsertAsync(CollectionName, chunk, vector);
        }

        return Results.Ok(new { file = file.FileName, chunksIngested = embedded.Count });
    }
    finally
    {
        File.Delete(tempPath);
    }
})
.DisableAntiforgery()
.WithName("IngestDocument");

app.MapPost("/query", async (
    QueryRequest request,
    EmbeddingService embeddingService,
    IVectorStore vectorStore,
    AnswerService answerService) =>
{
    var queryEmbedding = await embeddingService.EmbedTextAsync(request.Question);
    var topChunks = await vectorStore.SearchAsync(CollectionName, queryEmbedding, request.TopK ?? 5);

    if (!topChunks.Any())
        return Results.Ok(new { answer = "No relevant documents found.", sources = Array.Empty<string>() });

    var answer = await answerService.AnswerAsync(request.Question, topChunks);
    var sources = topChunks.Select(c => c.Chunk.SourceFile).Distinct();

    return Results.Ok(new { answer, sources });
})
.WithName("Query");

app.Run();

public partial class Program;

record QueryRequest(string Question, int? TopK = null);
