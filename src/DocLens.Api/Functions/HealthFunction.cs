using System.Net;
using DocLens.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace DocLens.Api.Functions;

public class HealthFunction(ILogger<HealthFunction> logger)
{
    [Function("Health")]
    [OpenApiOperation(operationId: "getHealth", tags: ["System"], Summary = "Health check", Description = "Returns the health status of the API")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(HealthResponse), Description = "Health status")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
    {
        logger.LogInformation("Health check requested");

        return new OkObjectResult(new HealthResponse(
            Status: "Healthy",
            Timestamp: DateTime.UtcNow,
            Version: "1.0.0"
        ));
    }
}
