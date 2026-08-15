# Testcontainers Fixtures for Integration Testing

## Overview

Integration tests that depend on external services (Ollama, PostgreSQL, etc.) use **Testcontainers** to provision ephemeral Docker containers for each test run. This ensures test repeatability, isolation, and CI/CD reliability.

## OllamaContainerFixture

**Location:** `tests/StreamingDigest.IntegrationTests/OllamaContainerFixture.cs`

### Purpose
Manages the lifecycle of an isolated Ollama container, handling:
- Container creation and startup (ollama/ollama:0.32.13)
- Model seeding (qwen2.5:0.5b pulled on init)
- Readiness verification (HTTP /api/tags polling)
- Graceful cleanup on dispose

### Usage Pattern

```csharp
public sealed class MyOllamaTests : IAsyncLifetime
{
    private readonly OllamaContainerFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact(Skip = "Requires Docker; runs locally only.")]
    public async Task MyTest()
    {
        var config = _fixture.CreateConfiguration();
        var endpoint = _fixture.Endpoint; // e.g., "http://localhost:11434"

        using var httpClient = new HttpClient { BaseAddress = new Uri(endpoint) };
        var client = new OllamaModelRuntimeClient(..., config);
        
        // Test your Ollama-dependent code
    }
}
```

### Configuration
- **Image Tag:** `ollama/ollama:0.32.13` (hardcoded for reproducibility)
- **Seeded Model:** `qwen2.5:0.5b` (tiny, ~500 MB — suitable for CI)
- **Seed Timeout:** 5 minutes (network/registry pulls)

### Tests Using This Fixture
- `OllamaModelRuntimeClientIntegrationTests`: Model listing, pulling, info
- `OllamaModelRuntimeClientTestcontainersTests`: Same coverage (Testcontainers reference)
- `MeaiOllamaIntegrationTests`: MEAI chat + embedding scenarios

## Running Tests Locally

Tests are skipped by default (require Docker + network access to Ollama registry).

To run locally:

```bash
# Remove Skip attribute from test method, then:
dotnet test tests/StreamingDigest.IntegrationTests \
  --filter "FullyQualifiedName~OllamaContainerFixture" \
  --configuration Release
```

## CI/CD Environment

GitHub Actions (ubuntu-latest) has Docker available by default. No additional setup required; tests will run when Skip is removed.

## Adding New Ollama Tests

1. Inherit from `IAsyncLifetime`
2. Create a private `_fixture = new OllamaContainerFixture()` field
3. Call `_fixture.InitializeAsync()` and `_fixture.DisposeAsync()` in lifecycle methods
4. Use `_fixture.Endpoint` and `_fixture.CreateConfiguration()` in test methods
5. Mark with `[Fact(Skip = "...")]` until Docker/network access is verified

## Troubleshooting

**"Ollama container did not become ready"**
- Docker is not running
- Network is offline (can't pull model from registry)
- Port 11434 is already in use

**"Port already in use"**
- Another test is running (tests run sequentially by xUnit)
- A previous container wasn't cleaned up (check `docker ps -a`)

**Model pull times out**
- Slow network or registry outage
- Increase `SeedPullTimeout` in OllamaContainerFixture if needed (currently 5 min)
