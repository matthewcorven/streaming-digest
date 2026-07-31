using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

public sealed class NotesCrudIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BootstrapUsername = "bootstrap-admin";
    private const string BootstrapPassword = "s3cr3t-passw0rd";
    private const string NewPassword = "new-passw0rd-123!";

    private readonly string _containerName = $"streaming-digest-notes-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        _factory = new NotesWebApplicationFactory(_connectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task Note_lifecycle_supports_create_get_list_update_and_delete()
    {
        using var client = CreateClient();
        await AuthenticateAsync(client);

        var targetId = Guid.NewGuid();

        // Create
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/notes");
        createRequest.Content = JsonContent.Create(new { targetType = "video", targetId, title = "Test Note", markdown = "Some **markdown** content." });
        createRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));

        using var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<NoteItemResponse>();
        Assert.NotNull(created);
        Assert.Equal("video", created!.TargetType);
        Assert.Equal(targetId, created.TargetId);
        Assert.Equal("Test Note", created.Title);
        Assert.Equal("Some **markdown** content.", created.Markdown);
        Assert.Equal("succeeded", created.EmbeddingStatus);

        // Get by ID
        using var getResponse = await client.GetAsync($"/api/notes/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<NoteItemResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);

        // List by target
        using var listResponse = await client.GetAsync($"/api/notes?targetType=video&targetId={targetId}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<NoteListResponse>();
        Assert.NotNull(list);
        Assert.Single(list!.Items);
        Assert.Equal(created.Id, list.Items[0].Id);

        // Update
        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/notes/{created.Id}");
        updateRequest.Content = JsonContent.Create(new { title = "Updated Title", markdown = "Updated markdown." });
        updateRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));
        using var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatePayload = await updateResponse.Content.ReadFromJsonAsync<NoteUpdateResponse>();
        Assert.NotNull(updatePayload);
        Assert.Equal("updated", updatePayload!.Status);
        Assert.Equal("Updated Title", updatePayload.Resource.Title);
        Assert.Equal("succeeded", updatePayload.Resource.EmbeddingStatus);

        // Delete (soft)
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/notes/{created.Id}");
        deleteRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));
        using var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Deleted note should return 404
        using var deletedGetResponse = await client.GetAsync($"/api/notes/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deletedGetResponse.StatusCode);

        // Deleted note should not appear in list
        using var listAfterDeleteResponse = await client.GetAsync($"/api/notes?targetType=video&targetId={targetId}");
        var listAfterDelete = await listAfterDeleteResponse.Content.ReadFromJsonAsync<NoteListResponse>();
        Assert.NotNull(listAfterDelete);
        Assert.Empty(listAfterDelete!.Items);
    }

    [Fact]
    public async Task Note_create_returns_409_when_live_note_already_exists_for_same_target()
    {
        using var client = CreateClient();
        await AuthenticateAsync(client);

        var targetId = Guid.NewGuid();
        var csrfToken = await GetCsrfTokenAsync(client);

        var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/notes");
        firstRequest.Content = JsonContent.Create(new { targetType = "segment", targetId, markdown = "First note." });
        firstRequest.Headers.Add("X-CSRF-Token", csrfToken);
        using var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var duplicateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/notes");
        duplicateRequest.Content = JsonContent.Create(new { targetType = "segment", targetId, markdown = "Duplicate note." });
        duplicateRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));
        using var duplicateResponse = await client.SendAsync(duplicateRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task Note_create_after_delete_succeeds_for_same_target()
    {
        using var client = CreateClient();
        await AuthenticateAsync(client);

        var targetId = Guid.NewGuid();

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/notes");
        createRequest.Content = JsonContent.Create(new { targetType = "repository", targetId, markdown = "Original note." });
        createRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));
        using var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<NoteItemResponse>();

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/notes/{created!.Id}");
        deleteRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));
        using var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var recreateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/notes");
        recreateRequest.Content = JsonContent.Create(new { targetType = "repository", targetId, markdown = "New note after delete." });
        recreateRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));
        using var recreateResponse = await client.SendAsync(recreateRequest);
        Assert.Equal(HttpStatusCode.Created, recreateResponse.StatusCode);
    }

    [Fact]
    public async Task Note_create_requires_markdown()
    {
        using var client = CreateClient();
        await AuthenticateAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/notes");
        request.Content = JsonContent.Create(new { targetType = "video", targetId = Guid.NewGuid(), markdown = "" });
        request.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Note_get_returns_404_for_unknown_id()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"/api/notes/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Note_delete_returns_404_for_unknown_id()
    {
        using var client = CreateClient();
        await AuthenticateAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/notes/{Guid.NewGuid()}");
        request.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task AuthenticateAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var csrfToken = await GetCsrfTokenAsync(client);
        using var changePasswordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePasswordRequest.Headers.Add("X-CSRF-Token", csrfToken);
        changePasswordRequest.Content = JsonContent.Create(new { currentPassword = BootstrapPassword, newPassword = NewPassword });

        var changePasswordResponse = await client.SendAsync(changePasswordRequest);
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var reloginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);
    }

    private async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(csrf);
        return csrf!.Token;
    }

    private HttpClient CreateClient()
    {
        return _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });
    }

    private async Task StartPostgresContainerAsync()
    {
        var dockerArgs = new[]
        {
            "run", "--rm", "-d", "--name", _containerName,
            "-e", $"POSTGRES_USER={Username}",
            "-e", $"POSTGRES_PASSWORD={Password}",
            "-e", $"POSTGRES_DB={DatabaseName}",
            "-p", $"{_hostPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL notes integration test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        try
        {
            await RunProcessAsync("docker", new[] { "rm", "-f", _containerName });
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString!);
                await connection.OpenAsync();
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the PostgreSQL notes integration test container to become ready.");
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start process '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}: {stderr}");
        }

        return stdout.Trim();
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class NotesWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:streamingdigest", connectionString);
            builder.UseSetting("ConnectionStrings:postgres", connectionString);
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:streamingdigest"] = connectionString,
                    ["ConnectionStrings:postgres"] = connectionString,
                    ["BOOTSTRAP_ADMIN_USERNAME"] = BootstrapUsername,
                    ["BOOTSTRAP_ADMIN_PASSWORD"] = BootstrapPassword
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingService>();
                services.AddSingleton<IEmbeddingService, FakeEmbeddingService>();
            });
        }
    }

    private sealed record NoteListResponse(IReadOnlyList<NoteItemResponse> Items);
    private sealed record NoteItemResponse(Guid Id, string TargetType, Guid TargetId, string? Title, string Markdown, string EmbeddingStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    private sealed record NoteUpdateResponse(string Status, string EntityType, Guid EntityId, NoteItemResponse Resource);
    private sealed record CsrfTokenResponse(string Token);
}
