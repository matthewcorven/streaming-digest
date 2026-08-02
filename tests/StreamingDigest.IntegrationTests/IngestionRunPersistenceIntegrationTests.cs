using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using Xunit;

namespace StreamingDigest.IntegrationTests;

[Collection("PostgreSQL Integration Tests")]
public sealed class IngestionRunPersistenceIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-ingestion-run-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private StreamingDigestDbContext? _context;
    private IngestionRunRepository? _repository;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        _context = new StreamingDigestDbContext(options);
        _repository = new IngestionRunRepository(_context);
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task CreateAndRetrieveIngestionRun()
    {
        // Arrange
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "scheduled",
            TriggeredBy = "system",
            Status = "pending",
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        await _repository!.AddAsync(run);
        var retrieved = await _repository.GetByIdAsync(run.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(run.Id, retrieved!.Id);
        Assert.Equal("scheduled", retrieved.RunType);
        Assert.Equal("pending", retrieved.Status);
    }

    [Fact]
    public async Task UpdateIngestionRunStatus()
    {
        // Arrange
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "scheduled",
            TriggeredBy = "system",
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _repository!.AddAsync(run);

        // Act
        await _repository.UpdateStatusAsync(run.Id, "completed");
        var updated = await _repository.GetByIdAsync(run.Id);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal("completed", updated!.Status);
    }

    [Fact]
    public async Task ListIngestionRunsOrderedByCreatedAt()
    {
        // Arrange
        var run1 = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "scheduled",
            TriggeredBy = "system",
            Status = "completed",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        var run2 = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "manual",
            TriggeredBy = "user",
            Status = "completed",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        await _repository!.AddAsync(run1);
        await _repository.AddAsync(run2);

        // Act
        var list = await _repository.GetListAsync(limit: 10);

        // Assert
        Assert.Equal(2, list.Count);
        Assert.Equal(run2.Id, list[0].Id); // Most recent first
        Assert.Equal(run1.Id, list[1].Id);
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL ingestion run integration test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL ingestion run integration test container to become ready.");
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
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}. STDERR: {stderr}");
        }

        return stdout.Trim();
    }

    private static int GetAvailablePort()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        return ((System.Net.IPEndPoint)socket.LocalEndPoint!).Port;
    }
}

[Collection("PostgreSQL Integration Tests")]
public sealed class IngestionItemPersistencePersistenceIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-ingestion-item-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private StreamingDigestDbContext? _context;
    private IngestionRunRepository? _runRepository;
    private IngestionItemRepository? _itemRepository;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        _context = new StreamingDigestDbContext(options);
        _runRepository = new IngestionRunRepository(_context);
        _itemRepository = new IngestionItemRepository(_context);
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task CreateAndRetrieveIngestionItem()
    {
        // Arrange
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "scheduled",
            TriggeredBy = "system",
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _runRepository!.AddAsync(run);

        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = run.Id,
            ItemType = "video",
            Stage = "transcript",
            Status = "completed",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TranscriptStatus = "completed"
        };

        // Act
        await _itemRepository!.AddAsync(item);
        var retrieved = await _itemRepository.GetByIdAsync(item.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(item.Id, retrieved!.Id);
        Assert.Equal(run.Id, retrieved.IngestionRunId);
        Assert.Equal("completed", retrieved.TranscriptStatus);
    }

    [Fact]
    public async Task CreateBulkIngestionItems()
    {
        // Arrange
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "scheduled",
            TriggeredBy = "system",
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _runRepository!.AddAsync(run);

        var items = Enumerable.Range(1, 5)
            .Select(_ => new IngestionItem
            {
                Id = Guid.NewGuid(),
                IngestionRunId = run.Id,
                ItemType = "video",
                Stage = "unknown",
                Status = "pending",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        // Act
        await _itemRepository!.AddBulkAsync(items);
        var retrieved = await _itemRepository.GetByRunIdAsync(run.Id);

        // Assert
        Assert.Equal(5, retrieved.Count);
    }

    [Fact]
    public async Task UpdateStageStatus()
    {
        // Arrange
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "scheduled",
            TriggeredBy = "system",
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _runRepository!.AddAsync(run);

        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = run.Id,
            ItemType = "video",
            Stage = "unknown",
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TranscriptStatus = "pending"
        };
        await _itemRepository!.AddAsync(item);

        // Act
        await _itemRepository.UpdateStageStatusAsync(item.Id, "transcript", "completed");
        var updated = await _itemRepository.GetByIdAsync(item.Id);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal("completed", updated!.TranscriptStatus);
    }

    [Fact]
    public async Task QueryItemsByStageStatus()
    {
        // Arrange
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "scheduled",
            TriggeredBy = "system",
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _runRepository!.AddAsync(run);

        var items = new[]
        {
            new IngestionItem
            {
                Id = Guid.NewGuid(),
                IngestionRunId = run.Id,
                ItemType = "video",
                Stage = "unknown",
                Status = "pending",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                TranscriptStatus = "completed"
            },
            new IngestionItem
            {
                Id = Guid.NewGuid(),
                IngestionRunId = run.Id,
                ItemType = "video",
                Stage = "unknown",
                Status = "pending",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                TranscriptStatus = "pending"
            },
            new IngestionItem
            {
                Id = Guid.NewGuid(),
                IngestionRunId = run.Id,
                ItemType = "video",
                Stage = "unknown",
                Status = "pending",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                TranscriptStatus = "failed"
            }
        };

        foreach (var item in items)
        {
            await _itemRepository!.AddAsync(item);
        }

        // Act
        var completed = await _itemRepository!.GetByRunIdWithStageStatusAsync(run.Id, "transcript", "completed");
        var pending = await _itemRepository.GetByRunIdWithStageStatusAsync(run.Id, "transcript", "pending");
        var failed = await _itemRepository.GetByRunIdWithStageStatusAsync(run.Id, "transcript", "failed");

        // Assert
        Assert.Single(completed);
        Assert.Single(pending);
        Assert.Single(failed);
    }

    [Fact]
    public async Task BulkUpdateStageStatus()
    {
        // Arrange
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "scheduled",
            TriggeredBy = "system",
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _runRepository!.AddAsync(run);

        var items = Enumerable.Range(1, 3)
            .Select(_ => new IngestionItem
            {
                Id = Guid.NewGuid(),
                IngestionRunId = run.Id,
                ItemType = "video",
                Stage = "unknown",
                Status = "pending",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                TranscriptStatus = "pending"
            })
            .ToList();

        foreach (var item in items)
        {
            await _itemRepository!.AddAsync(item);
        }

        // Act
        await _itemRepository!.BulkUpdateStageStatusAsync(run.Id, "transcript", "pending", "completed");
        var updated = await _itemRepository.GetByRunIdAsync(run.Id);

        // Assert
        Assert.All(updated, item => Assert.Equal("completed", item.TranscriptStatus));
    }

    [Fact]
    public async Task PerStageStatusIndependenceAcrossStages()
    {
        // Arrange
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "scheduled",
            TriggeredBy = "system",
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _runRepository!.AddAsync(run);

        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = run.Id,
            ItemType = "video",
            Stage = "unknown",
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _itemRepository!.AddAsync(item);

        // Act - Update different stages to different statuses
        await _itemRepository.UpdateStageStatusAsync(item.Id, "transcript", "completed");
        await _itemRepository.UpdateStageStatusAsync(item.Id, "segments", "pending");
        await _itemRepository.UpdateStageStatusAsync(item.Id, "screenshots", "failed");
        await _itemRepository.UpdateStageStatusAsync(item.Id, "embeddings", "pending");

        var updated = await _itemRepository.GetByIdAsync(item.Id);

        // Assert - Each stage should have its own status
        Assert.Equal("completed", updated!.TranscriptStatus);
        Assert.Equal("pending", updated.SegmentsStatus);
        Assert.Equal("failed", updated.ScreenshotsStatus);
        Assert.Equal("pending", updated.EmbeddingsStatus);
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL ingestion item integration test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL ingestion item integration test container to become ready.");
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
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}. STDERR: {stderr}");
        }

        return stdout.Trim();
    }

    private static int GetAvailablePort()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        return ((System.Net.IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
