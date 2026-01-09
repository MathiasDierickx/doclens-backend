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

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
