# Claude Code Instructions

## Development Principles

### TDD (Test-Driven Development)
- Write tests **before** implementing functionality
- Follow the Red-Green-Refactor cycle:
  1. **Red**: Write a failing test
  2. **Green**: Write minimal code to pass the test
  3. **Refactor**: Improve code while keeping tests green
- Each feature should have corresponding unit tests
- Use xUnit for .NET testing

### SOLID Principles
- **S**ingle Responsibility: Each class/function has one reason to change
- **O**pen/Closed: Open for extension, closed for modification
- **L**iskov Substitution: Subtypes must be substitutable for base types
- **I**nterface Segregation: Many specific interfaces over one general
- **D**ependency Inversion: Depend on abstractions, not concretions

### Dependency Injection (DI)
- Register all services in `Program.cs` using `builder.Services`
- Use constructor injection (not property or method injection)
- Prefer interfaces over concrete types for dependencies
- Use appropriate lifetimes:
  - `Singleton`: Stateless services, configuration, HTTP clients
  - `Scoped`: Per-request services (database contexts, unit of work)
  - `Transient`: Lightweight, stateless services
- Never use `new` for services that have dependencies - inject them instead

## Code Style

### .NET / C#
- Use dependency injection for services
- Extract interfaces for testability
- Keep Azure Functions thin - delegate to services
- Use records for DTOs and immutable data
- Prefer async/await for I/O operations

### TypeScript / React
- Use TypeScript strict mode
- Prefer functional components with hooks
- Keep components small and focused
- Use RTK Query for API calls (auto-generated from OpenAPI)

## Project Structure

### Backend Services
Services should be injected via DI and have interfaces:
```csharp
public interface IDocumentService { }
public class DocumentService : IDocumentService { }
```

### Testing
- Unit tests in `tests/DocLens.Api.Tests/`
- Name tests: `MethodName_Scenario_ExpectedResult`
- Mock external dependencies (Azure services, HTTP clients)

## Commits
- Use conventional commits (feat:, fix:, docs:, test:, refactor:)
