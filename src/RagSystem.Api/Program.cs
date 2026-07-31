// RagSystem.Api/Program.cs
using RagSystem.AI;
using RagSystem.Api;
using RagSystem.Core.Interfaces;
using RagSystem.Ingestion.Docs;
using RagSystem.Ingestion.Sql;
using RagSystem.VectorStore;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// Configuration values (from appsettings + user-secrets + env vars)
// ---------------------------------------------------------------------
var sqlAdminConnectionString = builder.Configuration.GetConnectionString("SqlServerAdmin")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:SqlServerAdmin");

var sqlReadOnlyConnectionString = builder.Configuration.GetConnectionString("SqlServerReadOnly")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:SqlServerReadOnly");

var openRouterApiKey = builder.Configuration["OpenRouter:ApiKey"]
    ?? throw new InvalidOperationException("Missing OpenRouter:ApiKey");

// Tables the NL2SQL/safety layer is allowed to reference — extend as you add sources
var allowedTables = new[] { "Customers", "Orders" };

// ---------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------
builder.Services.AddQdrantVectorStore(host: "localhost", port: 6334);

builder.Services.AddRagSystemAi(
    openRouterApiKey: openRouterApiKey,
    chatModel: "openai/gpt-4o-mini",
    embeddingModel: "text-embedding-3-small");

builder.Services.AddSingleton<IChunker, FixedSizeChunker>();
builder.Services.AddSingleton<IDocumentLoader, WordDocumentLoader>();

builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<AnswerService>();

builder.Services.AddSingleton(new SchemaIntrospector(sqlAdminConnectionString));
builder.Services.AddSingleton<SchemaCatalogBuilder>();
builder.Services.AddSingleton<Nl2SqlGenerator>();
builder.Services.AddSingleton(new SqlSafetyValidator(allowedTables));
builder.Services.AddSingleton(new SqlQueryExecutor(sqlReadOnlyConnectionString));

builder.Services.AddSingleton<QueryRouter>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string DocsCollection = "docs";
const string SchemaCollection = "schema_catalog";
const ulong VectorSize = 1536;

var app = builder.Build();

// ---------------------------------------------------------------------
// Startup: ensure Qdrant collections exist
// ---------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var vectorStore = (QdrantVectorStore)scope.ServiceProvider.GetRequiredService<IVectorStore>();
    await vectorStore.EnsureCollectionAsync(DocsCollection, VectorSize);
    await vectorStore.EnsureCollectionAsync(SchemaCollection, VectorSize);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ---------------------------------------------------------------------
// Endpoint: ingest a Word document
// ---------------------------------------------------------------------
app.MapPost("/ingest/documents", async (
    IFormFile file,
    IDocumentLoader loader,
    EmbeddingService embeddingService,
    IVectorStore vectorStore) =>
{
    if (!loader.CanHandle(file.FileName))
        return Results.BadRequest($"Unsupported file type: {file.FileName}");

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
            await vectorStore.UpsertAsync(DocsCollection, chunk, vector);
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

// ---------------------------------------------------------------------
// Endpoint: (re)build the schema catalog for NL2SQL
// ---------------------------------------------------------------------
app.MapPost("/ingest/schema", async (
    SchemaIntrospector introspector,
    SchemaCatalogBuilder catalogBuilder,
    EmbeddingService embeddingService,
    IVectorStore vectorStore) =>
{
    var schema = await introspector.GetSchemaAsync(allowedTables);
    var entries = catalogBuilder.BuildCatalogEntries(schema).ToList();

    foreach (var (tableName, text) in entries)
    {
        var embedding = await embeddingService.EmbedTextAsync(text);
        var chunk = new RagSystem.Core.Models.DocumentChunk
        {
            Id = $"schema_{tableName}",
            Content = text,
            SourceFile = "schema_catalog",
            ChunkIndex = 0
        };
        await vectorStore.UpsertAsync(SchemaCollection, chunk, embedding);
    }

    return Results.Ok(new { tablesIndexed = entries.Select(e => e.TableName) });
})
.WithName("IngestSchema");

// ---------------------------------------------------------------------
// Endpoint: main query — routes to Document or Database path
// ---------------------------------------------------------------------
app.MapPost("/query", async (
    QueryRequest request,
    QueryRouter router,
    EmbeddingService embeddingService,
    IVectorStore vectorStore,
    AnswerService answerService,
    Nl2SqlGenerator nl2Sql,
    SqlSafetyValidator validator,
    SqlQueryExecutor executor) =>
{
    var intent = await router.ClassifyAsync(request.Question);

    if (intent == QueryIntent.Document)
    {
        var queryEmbedding = await embeddingService.EmbedTextAsync(request.Question);
        var topChunks = await vectorStore.SearchAsync(DocsCollection, queryEmbedding, request.TopK ?? 5);

        if (!topChunks.Any())
            return Results.Ok(new { intent = "document", answer = "No relevant documents found.", sources = Array.Empty<string>() });

        var answer = await answerService.AnswerAsync(request.Question, topChunks);
        var sources = topChunks.Select(c => c.Chunk.SourceFile).Distinct();

        return Results.Ok(new { intent = "document", answer, sources });
    }
    else
    {
        var queryEmbedding = await embeddingService.EmbedTextAsync(request.Question);
        var relevantTables = await vectorStore.SearchAsync(SchemaCollection, queryEmbedding, topK: 3);
        var schemaContext = string.Join("\n\n", relevantTables.Select(t => t.Chunk.Content));

        var sql = await nl2Sql.GenerateSqlAsync(request.Question, schemaContext);

        var validation = validator.Validate(sql);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new
            {
                intent = "database",
                error = "Generated SQL failed safety validation.",
                detail = validation.Error,
                sql
            });
        }

        IEnumerable<dynamic> rows;
        try
        {
            rows = await executor.ExecuteAsync(sql);
        }
        catch (SqlException exception)
        {
            return Results.BadRequest(new
            {
                intent = "database",
                error = "Generated SQL could not be executed.",
                detail = exception.Message,
                sql
            });
        }

        var summaryPrompt = $"""
            Question: {request.Question}
            Query result (JSON): {System.Text.Json.JsonSerializer.Serialize(rows)}

            Answer the question in natural language based on this data.
            """;
        var finalAnswer = await answerService.AnswerRawAsync(summaryPrompt);

        return Results.Ok(new { intent = "database", answer = finalAnswer, sql });
    }
})
.WithName("Query");

app.Run();

record QueryRequest(string Question, int? TopK = null);
