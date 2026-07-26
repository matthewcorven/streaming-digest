using System.Collections.Concurrent;

namespace StreamingDigest.Application.Configuration;

public sealed class WorkerOperationConcurrencyController
{
    private readonly SemaphoreSlim _screenshotGate;
    private readonly HostScopedConcurrencyGate _websiteScrapeGate;

    public WorkerOperationConcurrencyController(WorkerConcurrencySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings;
        _screenshotGate = CreateGate(settings.ScreenshotConcurrency);
        _websiteScrapeGate = new HostScopedConcurrencyGate(
            settings.WebsiteScrapeGlobalConcurrency,
            settings.WebsiteScrapePerHostConcurrency);
    }

    public WorkerConcurrencySettings Settings { get; }

    public Task<T> RunScreenshotAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunAsync(_screenshotGate, operation, cancellationToken);
    }

    public Task RunScreenshotAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunAsync(_screenshotGate, operation, cancellationToken);
    }

    public Task<T> RunWebsiteScrapeAsync<T>(string url, Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(operation);

        return _websiteScrapeGate.RunAsync(url, operation, cancellationToken);
    }

    private static SemaphoreSlim CreateGate(int configuredLimit)
    {
        var normalizedLimit = NormalizeLimit(configuredLimit);
        return new SemaphoreSlim(normalizedLimit, normalizedLimit);
    }

    private static int NormalizeLimit(int configuredLimit) => configuredLimit > 0 ? configuredLimit : 1;

    private static async Task<T> RunAsync<T>(SemaphoreSlim gate, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            return await operation();
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task RunAsync(SemaphoreSlim gate, Func<Task> operation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            await operation();
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed class HostScopedConcurrencyGate
    {
        private readonly SemaphoreSlim _globalGate;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostGates = new(StringComparer.OrdinalIgnoreCase);
        private readonly int _perHostLimit;

        public HostScopedConcurrencyGate(int globalLimit, int perHostLimit)
        {
            var normalizedGlobalLimit = NormalizeLimit(globalLimit);
            _globalGate = new SemaphoreSlim(normalizedGlobalLimit, normalizedGlobalLimit);
            _perHostLimit = NormalizeLimit(perHostLimit);
        }

        public async Task<T> RunAsync<T>(string url, Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            var host = ResolveHost(url);

            await _globalGate.WaitAsync(cancellationToken);
            var hostGate = _hostGates.GetOrAdd(host, static (_, limit) => new SemaphoreSlim(limit, limit), _perHostLimit);
            await hostGate.WaitAsync(cancellationToken);

            try
            {
                return await operation();
            }
            finally
            {
                hostGate.Release();
                _globalGate.Release();
            }
        }

        private static string ResolveHost(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                return uri.Host;
            }

            throw new ArgumentException($"A valid absolute URL is required to apply host-scoped concurrency limits: '{url}'.", nameof(url));
        }
    }
}
