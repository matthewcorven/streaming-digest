using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamingDigest.Application.Services;
using StreamingDigest.Infrastructure.Services;

namespace StreamingDigest.Infrastructure.Services;

public static class ModelRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddModelRuntimeClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient<IModelRuntimeClient, OllamaModelRuntimeClient>(client =>
        {
            var ollamaEndpoint = ResolveOllamaEndpoint(configuration);
            if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
            {
                client.BaseAddress = new Uri(ollamaEndpoint.TrimEnd('/'), UriKind.Absolute);
            }
        });

        return services;
    }

    private static string? ResolveOllamaEndpoint(IConfiguration configuration)
    {
        var configKeys = new[] { "ollama:baseUrl", "ollama:endpoint", "llm:baseUrl", "embedding:ollamaEndpoint" };
        foreach (var key in configKeys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        var envVars = new[] { "OLLAMA_BASE_URL", "OLLAMA_HOST", "STREAMINGDIGEST_EMBEDDING_OLLAMA_ENDPOINT" };
        foreach (var variable in envVars)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "http://localhost:11434";
    }
}