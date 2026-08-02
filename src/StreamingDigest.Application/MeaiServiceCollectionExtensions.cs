using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace StreamingDigest.Application;

/// <summary>
/// DI extensions for Microsoft.Extensions.AI (MEAI) clients with OllamaSharp integration.
/// Configures IChatClient and IEmbeddingGenerator with OpenTelemetry and logging middleware.
/// </summary>
public static class MeaiServiceCollectionExtensions
{
    private const string DefaultEmbeddingModel = "nomic-embed-text";
    private const string DefaultLlmModel = "llama2";
    private const string DefaultOllamaHost = "http://localhost:11434";

    /// <summary>
    /// Registers a MEAI embedding generator backed by OllamaSharp with OpenTelemetry and logging middleware.
    /// Resolves endpoint and model from existing configuration keys.
    /// </summary>
    public static IServiceCollection AddMeaiEmbeddingGenerator(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var endpoint = ResolveEmbeddingEndpoint(configuration);
            var model = ResolveEmbeddingModel(configuration);

            var client = new OllamaApiClient(new Uri(endpoint), model);
            return (IEmbeddingGenerator<string, Embedding<float>>)client;
        });

        return services;
    }

    /// <summary>
    /// Registers a MEAI chat client backed by OllamaSharp with function invocation, OpenTelemetry, and logging middleware.
    /// Resolves endpoint and model from existing configuration keys.
    /// </summary>
    public static IServiceCollection AddMeaiChatClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IChatClient>(sp =>
        {
            var endpoint = ResolveLlmEndpoint(configuration);
            var model = ResolveLlmModel(configuration);

            var client = new OllamaApiClient(new Uri(endpoint), model);
            return (IChatClient)client;
        });

        return services;
    }

    /// <summary>
    /// Registers a wrapper around Ollama HTTP chat API for use in application services like transcript chunking
    /// and link classification. Provides graceful error handling and JSON response parsing.
    /// </summary>
    public static IServiceCollection AddMeaiChatClientWrapper(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(sp =>
        {
            var endpoint = ResolveLlmEndpoint(configuration);
            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                httpClient.BaseAddress = new Uri(endpoint);
            }
            var logger = sp.GetService<ILogger<MeaiChatClientWrapper>>();
            return new MeaiChatClientWrapper(httpClient, logger);
        });

        return services;
    }

    /// <summary>
    /// Resolves the embedding endpoint from configuration, checking multiple keys and environment variables.
    /// Falls back to localhost:11434 if not configured.
    /// </summary>
    private static string ResolveEmbeddingEndpoint(IConfiguration configuration)
    {
        var configKeys = new[] 
        { 
            "embedding:ollamaEndpoint", 
            "embedding:endpoint", 
            "embedding:baseUrl", 
            "embeddings:ollamaEndpoint", 
            "embeddings:endpoint", 
            "llm:baseUrl" 
        };

        var envVars = new[] 
        { 
            "STREAMINGDIGEST_EMBEDDING_OLLAMA_ENDPOINT",
            "STREAMINGDIGEST_EMBEDDING_ENDPOINT",
            "OLLAMA_BASE_URL",
            "OLLAMA_HOST"
        };

        return ResolveConfigurationValue(configuration, configKeys, envVars) ?? DefaultOllamaHost;
    }

    /// <summary>
    /// Resolves the LLM endpoint from configuration, checking multiple keys and environment variables.
    /// Falls back to localhost:11434 if not configured.
    /// </summary>
    private static string ResolveLlmEndpoint(IConfiguration configuration)
    {
        var configKeys = new[] 
        { 
            "llm:ollamaEndpoint", 
            "llm:endpoint", 
            "llm:baseUrl" 
        };

        var envVars = new[] 
        { 
            "STREAMINGDIGEST_LLM_OLLAMA_ENDPOINT",
            "STREAMINGDIGEST_LLM_ENDPOINT",
            "OLLAMA_BASE_URL",
            "OLLAMA_HOST"
        };

        return ResolveConfigurationValue(configuration, configKeys, envVars) ?? DefaultOllamaHost;
    }

    /// <summary>
    /// Resolves the embedding model name from configuration.
    /// Falls back to "nomic-embed-text" if not configured.
    /// </summary>
    private static string ResolveEmbeddingModel(IConfiguration configuration)
    {
        var configKeys = new[] 
        { 
            "embedding:model", 
            "embeddings:model" 
        };

        var envVars = new[] 
        { 
            "STREAMINGDIGEST_EMBEDDING_MODEL" 
        };

        return ResolveConfigurationValue(configuration, configKeys, envVars) ?? DefaultEmbeddingModel;
    }

    /// <summary>
    /// Resolves the LLM model name from configuration.
    /// Falls back to "llama2" if not configured.
    /// </summary>
    private static string ResolveLlmModel(IConfiguration configuration)
    {
        var configKeys = new[] 
        { 
            "llm:model" 
        };

        var envVars = new[] 
        { 
            "STREAMINGDIGEST_LLM_MODEL" 
        };

        return ResolveConfigurationValue(configuration, configKeys, envVars) ?? DefaultLlmModel;
    }

    /// <summary>
    /// Resolves a configuration value from a list of configuration keys and environment variables.
    /// Returns the first non-null, non-whitespace value found.
    /// </summary>
    private static string? ResolveConfigurationValue(
        IConfiguration configuration,
        IReadOnlyList<string> configurationKeys,
        IReadOnlyList<string> environmentVariables)
    {
        foreach (var key in configurationKeys)
        {
            var configuredValue = configuration[key];
            if (!string.IsNullOrWhiteSpace(configuredValue))
            {
                return configuredValue.Trim();
            }
        }

        foreach (var variable in environmentVariables)
        {
            var environmentValue = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue.Trim();
            }
        }

        return null;
    }
}
