using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

public sealed class ChannelCrudIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BootstrapUsername = "bootstrap-admin";
    private const string BootstrapPassword = "s3cr3t-passw0rd";
    private const string NewPassword = "new-passw0rd-123!";

    private readonly string _containerName = $"streaming-digest-channel-tests-{Guid.NewGuid():N}";
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

        _factory = new ChannelCrudWebApplicationFactory(_connectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task Channel_lifecycle_supports_create_list_get_update_and_delete()
    {
        using var client = CreateClient();
        await AuthenticateAsync(client);

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/channels");
        createRequest.Content = JsonContent.Create(new { sourceUrl = "https://www.youtube.com/@example", defaultMaxAgeDays = 30, defaultBackfillMaxVideos = 100 });
        createRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));

        using var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdChannel = await createResponse.Content.ReadFromJsonAsync<ChannelDetailResponse>();
        Assert.NotNull(createdChannel);
        Assert.Equal("example", createdChannel!.YoutubeChannelId);
        Assert.Equal(30, createdChannel.IngestionDefaults.MaxAgeDays);
        Assert.Equal(100, createdChannel.IngestionDefaults.BackfillMaxVideos);

        using var listResponse = await client.GetAsync("/api/channels");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var list = await listResponse.Content.ReadFromJsonAsync<ChannelListResponse>();
        Assert.NotNull(list);
        Assert.Single(list!.Items);
        Assert.Equal(createdChannel.Id, list.Items[0].Id);

        using var getResponse = await client.GetAsync($"/api/channels/{createdChannel.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetchedChannel = await getResponse.Content.ReadFromJsonAsync<ChannelDetailResponse>();
        Assert.NotNull(fetchedChannel);
        Assert.Equal(createdChannel.Id, fetchedChannel!.Id);

        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/channels/{createdChannel.Id}");
        updateRequest.Content = JsonContent.Create(new { nameOverride = "Preferred Name", descriptionOverride = "Optional override", isPaused = false, defaultMaxAgeDays = 45, defaultBackfillMaxVideos = 200 });
        updateRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));

        using var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedPayload = await updateResponse.Content.ReadFromJsonAsync<ChannelUpdateResponse>();
        Assert.NotNull(updatedPayload);
        Assert.Equal("updated", updatedPayload!.Status);
        Assert.Equal("Preferred Name", updatedPayload.Resource.Name.Override);
        Assert.Equal(45, updatedPayload.Resource.IngestionDefaults.MaxAgeDays);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/channels/{createdChannel.Id}?deleteRelatedData=false");
        deleteRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));

        using var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var deletedGetResponse = await client.GetAsync($"/api/channels/{createdChannel.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deletedGetResponse.StatusCode);
    }

    [Fact]
    public async Task Channel_delete_requires_confirmation_for_destructive_delete_requests()
    {
        using var client = CreateClient();
        await AuthenticateAsync(client);

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/channels");
        createRequest.Content = JsonContent.Create(new { sourceUrl = "https://www.youtube.com/@example" });
        createRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));

        using var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdChannel = await createResponse.Content.ReadFromJsonAsync<ChannelDetailResponse>();
        Assert.NotNull(createdChannel);

        using var destructiveDeleteWithoutConfirmationRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/channels/{createdChannel!.Id}?deleteRelatedData=true");
        destructiveDeleteWithoutConfirmationRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));

        using var destructiveDeleteWithoutConfirmationResponse = await client.SendAsync(destructiveDeleteWithoutConfirmationRequest);
        Assert.Equal(HttpStatusCode.BadRequest, destructiveDeleteWithoutConfirmationResponse.StatusCode);

        using var destructiveDeleteWithConfirmationRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/channels/{createdChannel.Id}?deleteRelatedData=true&confirm=true");
        destructiveDeleteWithConfirmationRequest.Headers.Add("X-CSRF-Token", await GetCsrfTokenAsync(client));

        using var destructiveDeleteWithConfirmationResponse = await client.SendAsync(destructiveDeleteWithConfirmationRequest);
        Assert.Equal(HttpStatusCode.OK, destructiveDeleteWithConfirmationResponse.StatusCode);

        using var deletedGetResponse = await client.GetAsync($"/api/channels/{createdChannel.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deletedGetResponse.StatusCode);
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
            "run",
            "--rm",
            "-d",
            "--name",
            _containerName,
            "-e",
            $"POSTGRES_USER={Username}",
            "-e",
            $"POSTGRES_PASSWORD={Password}",
            "-e",
            $"POSTGRES_DB={DatabaseName}",
            "-p",
            $"{_hostPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL channel integration test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL channel integration test container to become ready.");
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

    private sealed class ChannelCrudWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
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
        }
    }

    private sealed record ChannelListResponse(IReadOnlyList<ChannelListItemResponse> Items, int Page, int PageSize, int TotalCount);

    private sealed record ChannelUpdateResponse(string Status, string EntityType, Guid EntityId, ChannelDetailResponse Resource);

    private sealed record ChannelDetailResponse(Guid Id, string YoutubeChannelId, ChannelValueResponse Name, ChannelValueResponse Description, string ProfileUrl, string SourceUrl, bool IsPaused, bool IsDegraded, int ConsecutiveFailures, DateTimeOffset? LastIngestedAt, string? LastIngestionStatus, ChannelIngestionDefaultsResponse IngestionDefaults);

    private sealed record ChannelListItemResponse(Guid Id, string YoutubeChannelId, string Name, string ProfileUrl, bool IsPaused, bool IsDegraded, int ConsecutiveFailures, DateTimeOffset? LastIngestedAt, string? LastIngestionStatus);

    private sealed record ChannelValueResponse(string? Original, string? Override, string? Effective);

    private sealed record ChannelIngestionDefaultsResponse(int? MaxAgeDays, int? BackfillMaxVideos);

    private sealed record CsrfTokenResponse(string Token);
}
