# Testcontainers for Ollama Integration Testing

## Overview

This project uses **Testcontainers** to provide deterministic, isolated, and reproducible Ollama integration testing. Instead of relying on manual Ollama setup or fragile docker commands, tests can spawn isolated Ollama containers automatically with full lifecycle management.

## Why Testcontainers?

- **Automatic Container Lifecycle**: Containers are started before tests and cleaned up after, without manual intervention
- **Port Isolation**: Each test gets its own Ollama instance on an ephemeral port—no port conflicts
- **Reproducible CI/CD**: Tests pass consistently in clean environments, including GitHub Actions runners
- **Deterministic**: No dependency on pre-installed or pre-configured Ollama; each test is self-contained

## Dependencies

Testcontainers is managed via Central Package Management in `Directory.Packages.props`:

```xml
<PackageVersion Include="Testcontainers" Version="4.14.0" />
<PackageVersion Include="Testcontainers.Ollama" Version="4.14.0" />
```

These are already added to the project. Test projects reference them via:

```xml
<PackageReference Include="Testcontainers" />
<PackageReference Include="Testcontainers.Ollama" />
```

## Using OllamaTestFixture

The `OllamaTestFixture` class (`tests/StreamingDigest.Integration.TestContainers/OllamaTestContainer.cs`) provides a managed Ollama container for tests.

### Basic Setup

```csharp
using StreamingDigest.Integration.TestContainers;
using Xunit;

public class MyOllamaTests : IAsyncLifetime
{
    private OllamaTestContainer _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new OllamaTestContainer();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task MyTest()
    {
        // Container is running; _fixture provides:
        // - OllamaUri: http://<host>:<port>
        // - HttpClient: Pre-configured client targeting OllamaUri
        // - WaitForReadyAsync(): Waits for container health check
        // - PullModelAsync(modelName): Pulls a model (requires network access)
        // - ListModelsAsync(): Lists available models

        var uri = _fixture.OllamaUri;
        var client = _fixture.HttpClient;

        // Use client to make requests to Ollama
        var response = await client.PostAsync("/api/health", null);
        Assert.True(response.IsSuccessStatusCode);
    }
}
```

### Using with Dependency Injection (xUnit Collection Fixtures)

For shared fixtures across multiple test classes:

```csharp
public class OllamaFixture : OllamaTestContainer { }

[CollectionDefinition("Ollama collection")]
public class OllamaCollection : ICollectionFixture<OllamaFixture>
{
    // This class is intentionally empty
}

[Collection("Ollama collection")]
public class Test1
{
    private readonly OllamaFixture _fixture;

    public Test1(OllamaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MyTest()
    {
        // _fixture is already initialized and will be shared across Test1 and Test2
        var uri = _fixture.OllamaUri;
        // ...
    }
}

[Collection("Ollama collection")]
public class Test2
{
    private readonly OllamaFixture _fixture;

    public Test2(OllamaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AnotherTest()
    {
        // Same fixture instance as Test1
    }
}
```

## OllamaTestFixture Interface

`IOllamaTestFixture` defines the contract:

```csharp
public interface IOllamaTestFixture
{
    /// <summary>
    /// Gets the Ollama endpoint URI (e.g., http://127.0.0.1:12345).
    /// </summary>
    Uri OllamaUri { get; }

    /// <summary>
    /// Gets an HttpClient pre-configured with BaseAddress = OllamaUri.
    /// </summary>
    HttpClient HttpClient { get; }

    /// <summary>
    /// Waits for the Ollama container to be ready (health check polling).
    /// </summary>
    Task WaitForReadyAsync(CancellationToken ct = default);

    /// <summary>
    /// Pulls a model into the Ollama container.
    /// Yields ModelPullProgress updates for streaming pull status.
    /// Requires network access to the Ollama registry.
    /// </summary>
    IAsyncEnumerable<ModelPullProgress> PullModelAsync(
        string modelName,
        [EnumeratorCancellation] CancellationToken ct = default);

    /// <summary>
    /// Lists all models in the Ollama container.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default);
}
```

## Test Project Structure

- `tests/StreamingDigest.Integration.TestContainers/OllamaTestContainer.cs` — Core fixture implementation
- `tests/StreamingDigest.Integration.TestContainers/IOllamaTestFixture.cs` — Interface definition and DTOs
- `tests/StreamingDigest.Integration.TestContainers/OllamaTestContainerTests.cs` — Sample integration tests

Sample test categories:

1. **Lifecycle Tests**: Container starts, is ready, and shuts down cleanly
2. **Model Management Tests**: Models can be pulled and listed
3. **App Integration Tests**: Services like `OllamaModelRuntimeClient` can connect to the container

## CI/CD Integration

### GitHub Actions

Testcontainers requires Docker. GitHub Actions runners (ubuntu-latest, windows-latest, macos-latest) come with Docker pre-installed.

To run integration tests in CI:

```yaml
- name: Run integration tests with Testcontainers
  run: dotnet test tests/StreamingDigest.Integration.TestContainers/ -v normal
```

### Local Development

Ensure Docker is running:

```bash
# Check Docker is available
docker ps

# Run Testcontainers tests
dotnet test tests/StreamingDigest.Integration.TestContainers/ -v normal
```

## Debugging and Troubleshooting

### Container Logs

Debug output includes container lifecycle events. Enable verbose logging:

```bash
dotnet test tests/StreamingDigest.Integration.TestContainers/ -v detailed
```

### Docker Desktop

Watch container creation and cleanup in Docker Desktop:

```bash
# In another terminal, watch containers in real-time
docker ps -a --format "table {{.ID}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}"
```

### Network Access Issues

Some tests (model pulling) require network access to the Ollama registry. These are marked `[Fact(Skip = "...")]` by default. To run them locally:

```bash
dotnet test tests/StreamingDigest.Integration.TestContainers/ --filter "PullModel" -v normal
```

### Port Conflicts

If you see errors about ports being in use, ensure:
1. No other Ollama containers are running: `docker ps | grep ollama`
2. Testcontainers can bind ephemeral ports (usually not an issue on local dev machines)

## Best Practices

1. **Always await InitializeAsync/DisposeAsync**: xUnit calls these automatically for `IAsyncLifetime`, but manually-created fixtures must be disposed.

2. **Share fixtures when appropriate**: Use xUnit collection fixtures to share a single Ollama container across multiple test classes if they don't require isolation.

3. **Document network prerequisites**: Mark tests that require network access (e.g., pulling large models) with `[Fact(Skip = "...")]` and clear reasoning.

4. **Keep tests focused**: Each test should verify one aspect of Ollama integration (health check, model management, client connection, etc.).

5. **Use WaitForReadyAsync**: Call `_fixture.WaitForReadyAsync()` before making requests if you need to ensure the container is fully healthy.

## References

- [Testcontainers .NET Documentation](https://testcontainers.com/docs/languages/dotnet/)
- [Testcontainers.Ollama Module](https://testcontainers.com/modules/ollama/)
- [Ollama HTTP API](https://github.com/ollama/ollama/blob/main/docs/api.md)

## Example: Writing a New Integration Test

```csharp
using StreamingDigest.Integration.TestContainers;
using Xunit;

namespace StreamingDigest.IntegrationTests
{
    public class MyNewOllamaTests : IAsyncLifetime
    {
        private OllamaTestContainer _fixture = null!;

        public async Task InitializeAsync()
        {
            _fixture = new OllamaTestContainer();
            await _fixture.InitializeAsync();
        }

        public async Task DisposeAsync()
        {
            await _fixture.DisposeAsync();
        }

        [Fact]
        public async Task HealthEndpoint_ReturnsOk()
        {
            await _fixture.WaitForReadyAsync();
            
            var response = await _fixture.HttpClient.PostAsync("/api/health", null);
            Assert.True(response.IsSuccessStatusCode);
        }

        [Fact]
        public async Task GenerateEndpoint_ReturnsValidResponse()
        {
            await _fixture.WaitForReadyAsync();

            var content = new StringContent(
                @"{""model"":""ollama:latest"",""prompt"":""hello""}",
                Encoding.UTF8,
                "application/json"
            );

            var response = await _fixture.HttpClient.PostAsync("/api/generate", content);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
    }
}
```

## Next Steps

- Add Testcontainers fixtures to existing Ollama integration tests
- Wire `OllamaTestContainer` into CI/CD workflows
- Consider shared fixture strategies for test suite optimization
