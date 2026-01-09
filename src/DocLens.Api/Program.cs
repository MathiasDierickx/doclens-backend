using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Data.Tables;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using DocLens.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Add OpenAPI support
builder.Services.AddSingleton<Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions.IOpenApiConfigurationOptions>(_ =>
    new Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations.OpenApiConfigurationOptions
    {
        Info = new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "DocLens API",
            Version = "1.0.0",
            Description = "Document Q&A API - Upload PDFs and ask questions about their content"
        },
        Servers = Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations.DefaultOpenApiConfigurationOptions.GetHostNames(),
        OpenApiVersion = Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums.OpenApiVersionType.V3
    });

// Register AI Services
builder.Services.AddSingleton<DocumentAnalysisClient>(_ =>
{
    var endpoint = Environment.GetEnvironmentVariable("DocumentIntelligenceEndpoint")!;
    var key = Environment.GetEnvironmentVariable("DocumentIntelligenceKey")!;
    return new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key));
});

builder.Services.AddSingleton(sp =>
{
    var endpoint = Environment.GetEnvironmentVariable("AzureOpenAIEndpoint")!;
    var key = Environment.GetEnvironmentVariable("AzureOpenAIKey")!;
    return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<AzureOpenAIClient>();
    var deploymentName = Environment.GetEnvironmentVariable("AzureOpenAIEmbeddingDeployment")!;
    return client.GetEmbeddingClient(deploymentName);
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<AzureOpenAIClient>();
    var deploymentName = Environment.GetEnvironmentVariable("AzureOpenAIChatDeployment")!;
    return client.GetChatClient(deploymentName);
});

builder.Services.AddSingleton<SearchIndexClient>(_ =>
{
    var endpoint = Environment.GetEnvironmentVariable("AzureSearchEndpoint")!;
    var key = Environment.GetEnvironmentVariable("AzureSearchKey")!;
    return new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(key));
});

builder.Services.AddSingleton<SearchClient>(sp =>
{
    var endpoint = Environment.GetEnvironmentVariable("AzureSearchEndpoint")!;
    var key = Environment.GetEnvironmentVariable("AzureSearchKey")!;
    var indexName = Environment.GetEnvironmentVariable("AzureSearchIndexName")!;
    return new SearchClient(new Uri(endpoint), indexName, new AzureKeyCredential(key));
});

builder.Services.AddSingleton<TableClient>(_ =>
{
    var connectionString = Environment.GetEnvironmentVariable("StorageConnection")!;
    var tableClient = new TableClient(connectionString, "IndexingStatus");
    tableClient.CreateIfNotExists();
    return tableClient;
});

// Register application services
builder.Services.AddSingleton<IChunkingService, ChunkingService>();
builder.Services.AddSingleton<IDocumentIntelligenceService, DocumentIntelligenceService>();
builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
builder.Services.AddSingleton<IIndexingStatusService, IndexingStatusService>();
builder.Services.AddSingleton<ISearchService>(sp =>
{
    var searchClient = sp.GetRequiredService<SearchClient>();
    var indexClient = sp.GetRequiredService<SearchIndexClient>();
    var indexName = Environment.GetEnvironmentVariable("AzureSearchIndexName")!;
    return new SearchService(searchClient, indexClient, indexName);
});

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
