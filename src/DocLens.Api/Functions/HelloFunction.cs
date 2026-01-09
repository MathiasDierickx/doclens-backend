using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DocLens.Api.Functions;

public class HelloFunction(ILogger<HelloFunction> logger)
{
    [Function("Hello")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hello")] HttpRequest req)
    {
        logger.LogInformation("Hello endpoint called");

        var name = req.Query["name"].FirstOrDefault() ?? "World";

        return new OkObjectResult(new
        {
            Message = $"Hello, {name}! Welcome to DocLens API.",
            Timestamp = DateTime.UtcNow
        });
    }
}
