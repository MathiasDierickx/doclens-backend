using System.Net;
using DocLens.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace DocLens.Api.Functions;

public class HelloFunction(ILogger<HelloFunction> logger)
{
    [Function("Hello")]
    [OpenApiOperation(operationId: "sayHello", tags: ["System"], Summary = "Hello world", Description = "Returns a greeting message")]
    [OpenApiParameter(name: "name", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Name to greet")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(HelloResponse), Description = "Greeting message")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hello")] HttpRequest req)
    {
        logger.LogInformation("Hello endpoint called");

        var name = req.Query["name"].FirstOrDefault() ?? "World";

        return new OkObjectResult(new HelloResponse(
            Message: $"Hello, {name}! Welcome to DocLens API.",
            Timestamp: DateTime.UtcNow
        ));
    }
}
