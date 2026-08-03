using Aspire.Hosting;
using Aspire.Hosting.Docker;
using Aspire.Hosting.Docker.Resources;
using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using System.Globalization;

var builder = DistributedApplication.CreateBuilder(args);

const string composeProjectName = "streaming-digest";
const string defaultEmbeddingModel = "bge-m3";
const string defaultLlmModel = "llama3.1:8b";
const string ollamaDataVolumeName = "streamingdigest-ollama-data";
// Whisper (audio-to-text) runtime — issue #210.
// The whisper service is an OPTIONAL runtime: caption-less videos need it; captioned
// ingestion proceeds with a warning when it is absent (PRD §2.4). For that reason api/worker
// do NOT WaitFor(whisper). The image/tag are parameterized so the model-download plan can
// swap in the verified community whisper.cpp HTTP image without touching this wiring.
// TODO(model-download-implementation-plan): replace placeholder image with the verified
// community whisper.cpp HTTP server image once acquisition/verification lands.
const string whisperImage = "ghcr.io/fedir/whisper-cpp-server";
const string whisperImageTag = "latest";
const int whisperPort = 8080;

var postgresUsername = builder.AddParameterFromConfiguration(
    "postgres-username",
    "Parameters:postgres-username");
var postgresPassword = builder.AddParameterFromConfiguration(
    "postgres-password",
    "Parameters:postgres-password",
    secret: true);
var grafanaAdminUser = builder.AddParameterFromConfiguration(
    "grafana-admin-user",
    "Parameters:grafana-admin-user");
var grafanaAdminPassword = builder.AddParameterFromConfiguration(
    "grafana-admin-password",
    "Parameters:grafana-admin-password",
    secret: true);
var pgAdminDefaultEmail = builder.AddParameterFromConfiguration(
    "pgadmin-default-email",
    "Parameters:pgadmin-default-email");
var pgAdminDefaultPassword = builder.AddParameterFromConfiguration(
    "pgadmin-default-password",
    "Parameters:pgadmin-default-password",
    secret: true);

builder.AddDockerComposeEnvironment("docker-compose")
    .WithDashboard(false)
    .ConfigureEnvFile(env =>
    {
        SetEnvDefault(env, "GRAFANA_BINDMOUNT_0", "./compose/observability/grafana/provisioning");
        SetEnvDefault(env, "GRAFANA_BINDMOUNT_1", "./compose/observability/grafana/dashboards");
        SetEnvDefault(env, "LOKI_BINDMOUNT_0", "./compose/observability/loki-config.yaml");
        SetEnvDefault(env, "OTEL_COLLECTOR_BINDMOUNT_0", "./compose/observability/otel-collector.yaml");
        SetEnvDefault(env, "PROMETHEUS_BINDMOUNT_0", "./compose/observability/prometheus.yml");
        SetEnvDefault(env, "TEMPO_BINDMOUNT_0", "./compose/observability/tempo.yaml");
        env.Remove("API_IMAGE");
        env.Remove("API_PORT");
        env.Remove("SCRAPER_IMAGE");
        env.Remove("WORKER_IMAGE");
    })
    .ConfigureComposeFile(composeFile =>
    {
        composeFile.Name = composeProjectName;
        composeFile.AddVolume(new Volume { Name = "streaming-digest-media", Driver = "local", External = false });
        composeFile.AddVolume(new Volume { Name = "streaming-digest-debug-html", Driver = "local", External = false });
        composeFile.AddVolume(new Volume { Name = "streaming-digest-matrix-state", Driver = "local", External = false });

        SetContainerName(composeFile, "postgres", "streaming-digest-postgres");
        SetContainerName(composeFile, "ollama", "streaming-digest-ollama");
        SetContainerName(composeFile, "ollama-bootstrap", "streaming-digest-ollama-bootstrap");
        SetContainerName(composeFile, "otel-collector", "streaming-digest-otel-collector");
        SetContainerName(composeFile, "prometheus", "streaming-digest-prometheus");
        SetContainerName(composeFile, "grafana", "streaming-digest-grafana");
        SetContainerName(composeFile, "pgadmin", "streaming-digest-pgadmin");
        SetContainerName(composeFile, "loki", "streaming-digest-loki");
        SetContainerName(composeFile, "tempo", "streaming-digest-tempo");
        SetContainerName(composeFile, "scraper", "streaming-digest-scraper");
        SetContainerName(composeFile, "api", "streaming-digest-api");
        SetContainerName(composeFile, "worker", "streaming-digest-worker");
        SetContainerName(composeFile, "whisper", "streaming-digest-whisper");

        if (composeFile.Services.TryGetValue("api", out var apiService))
        {
            apiService.Image = "streaming-digest-api";
            apiService.Build = new Build
            {
                Context = ".",
                Dockerfile = "src/StreamingDigest.Api/Dockerfile"
            };

            apiService.Environment?.Remove("HTTP_PORTS");
            apiService.AddEnvironmentalVariable("ASPNETCORE_URLS", "http://0.0.0.0:8080");
            apiService.AddEnvironmentalVariable("ASPNETCORE_ENVIRONMENT", "Production");
            apiService.AddEnvironmentalVariable("notifications__matrix__enabled", "${NOTIFICATIONS_MATRIX_ENABLED:-false}");
            apiService.AddEnvironmentalVariable("notifications__matrix__homeserverUrl", "${NOTIFICATIONS_MATRIX_HOMESERVER_URL:-https://matrix-client.matrix.org}");
            apiService.AddEnvironmentalVariable("notifications__matrix__accessToken", "${NOTIFICATIONS_MATRIX_ACCESS_TOKEN:-}");
            apiService.AddEnvironmentalVariable("notifications__matrix__roomId", "${NOTIFICATIONS_MATRIX_ROOM_ID:-}");
            apiService.AddEnvironmentalVariable("notifications__matrix__botUserId", "${NOTIFICATIONS_MATRIX_BOT_USER_ID:-}");
            apiService.AddEnvironmentalVariable("notifications__matrix__dashboardBaseUrl", "${NOTIFICATIONS_MATRIX_DASHBOARD_BASE_URL:-http://localhost:8080}");
            apiService.AddEnvironmentalVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317");
            apiService.AddEnvironmentalVariable("STREAMINGDIGEST_WHISPER_BASE_URL", "http://whisper:" + whisperPort.ToString(CultureInfo.InvariantCulture));
            apiService.Ports = ["8080:8080"];
        }

        if (composeFile.Services.TryGetValue("worker", out var workerService))
        {
            workerService.Image = "streaming-digest-worker";
            workerService.Build = new Build
            {
                Context = ".",
                Dockerfile = "src/StreamingDigest.Worker/Dockerfile"
            };

            workerService.AddEnvironmentalVariable("ASPNETCORE_ENVIRONMENT", "Production");
            workerService.AddEnvironmentalVariable("notifications__matrix__enabled", "${NOTIFICATIONS_MATRIX_ENABLED:-false}");
            workerService.AddEnvironmentalVariable("notifications__matrix__homeserverUrl", "${NOTIFICATIONS_MATRIX_HOMESERVER_URL:-https://matrix-client.matrix.org}");
            workerService.AddEnvironmentalVariable("notifications__matrix__accessToken", "${NOTIFICATIONS_MATRIX_ACCESS_TOKEN:-}");
            workerService.AddEnvironmentalVariable("notifications__matrix__roomId", "${NOTIFICATIONS_MATRIX_ROOM_ID:-}");
            workerService.AddEnvironmentalVariable("notifications__matrix__botUserId", "${NOTIFICATIONS_MATRIX_BOT_USER_ID:-}");
            workerService.AddEnvironmentalVariable("notifications__matrix__dashboardBaseUrl", "${NOTIFICATIONS_MATRIX_DASHBOARD_BASE_URL:-http://localhost:8080}");
            workerService.AddEnvironmentalVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317");
            workerService.AddEnvironmentalVariable("STREAMINGDIGEST_WHISPER_BASE_URL", "http://whisper:" + whisperPort.ToString(CultureInfo.InvariantCulture));
            workerService.AddVolume(new Volume { Name = "streaming-digest-media", Source = "streaming-digest-media", Target = "/var/lib/streaming-digest/media", Type = "volume", ReadOnly = false });
            workerService.AddVolume(new Volume { Name = "streaming-digest-debug-html", Source = "streaming-digest-debug-html", Target = "/var/lib/streaming-digest/raw-html", Type = "volume", ReadOnly = false });
            workerService.AddVolume(new Volume { Name = "streaming-digest-matrix-state", Source = "streaming-digest-matrix-state", Target = "/var/lib/streaming-digest/matrix", Type = "volume", ReadOnly = false });
        }

        if (composeFile.Services.TryGetValue("scraper", out var scraperService))
        {
            scraperService.Image = "streaming-digest-scraper";
            scraperService.Build = new Build
            {
                Context = "./src/StreamingDigest.Scraper"
            };

            scraperService.Environment?.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
            scraperService.Environment?.Remove("OTEL_EXPORTER_OTLP_PROTOCOL");
            scraperService.Environment?.Remove("OTEL_SERVICE_NAME");
            scraperService.AddVolume(new Volume { Name = "streaming-digest-media", Source = "streaming-digest-media", Target = "/var/lib/streaming-digest/media", Type = "volume", ReadOnly = false });
            scraperService.AddVolume(new Volume { Name = "streaming-digest-debug-html", Source = "streaming-digest-debug-html", Target = "/var/lib/streaming-digest/raw-html", Type = "volume", ReadOnly = false });
        }

        if (composeFile.Services.TryGetValue("grafana", out var grafanaService))
        {
            grafanaService.AddEnvironmentalVariable("GF_SERVER_ROOT_URL", "%(protocol)s://%(domain)s:%(http_port)s/grafana/");
            grafanaService.AddEnvironmentalVariable("GF_SERVER_SERVE_FROM_SUB_PATH", "true");
                grafanaService.AddEnvironmentalVariable("GF_AUTH_ANONYMOUS_ENABLED", "true");
                grafanaService.AddEnvironmentalVariable("GF_AUTH_ANONYMOUS_ORG_ROLE", "Viewer");
        }

        SetPublishedPorts(composeFile, "ollama", ["127.0.0.1:11434:11434"]);
        SetPublishedPorts(composeFile, "otel-collector", ["4317:4317", "4318:4318"]);
        SetPublishedPorts(composeFile, "prometheus", ["127.0.0.1:9090:9090"]);
        SetPublishedPorts(composeFile, "grafana", ["127.0.0.1:3000:3000"]);
        SetPublishedPorts(composeFile, "pgadmin", ["127.0.0.1:5050:5050"]);
        SetPublishedPorts(composeFile, "loki", ["127.0.0.1:3100:3100"]);
        SetPublishedPorts(composeFile, "tempo", ["127.0.0.1:3200:3200"]);
        SetPublishedPorts(composeFile, "whisper", [$"127.0.0.1:{whisperPort}:{whisperPort}"]);
    });

var postgres = builder.AddPostgres("postgres", postgresUsername, postgresPassword)
    // pgvector is required (ARCHITECTURE.md target runtime: "PostgreSQL + pgvector").
    // The pgvector image bundles the `vector` extension on top of stock postgres.
    .WithImage("pgvector/pgvector")
    .WithImageTag("0.8.5-pg18-trixie")
    .WithImageRegistry("docker.io")
    .WithVolume("streamingdigest-postgres18-data", "/var/lib/postgresql")
    .AddDatabase("streamingdigest");

var ollama = builder.AddContainer("ollama", "ollama/ollama")
    .WithImageTag("latest")
    .WithEnvironment("OLLAMA_HOST", "0.0.0.0:11434")
    .WithVolume(ollamaDataVolumeName, "/root/.ollama")
    .WithEndpoint(targetPort: 11434, port: 11434, scheme: "http", name: "http", isExternal: true)
    .WithHttpHealthCheck("/api/tags");

var ollamaBootstrap = builder.AddContainer("ollama-bootstrap", "ollama/ollama")
    .WithImageTag("latest")
    .WithVolume(ollamaDataVolumeName, "/root/.ollama")
    .WithEntrypoint("/bin/sh")
    .WithArgs(
        "-c",
        $"ollama serve & ollama_pid=$!; until ollama list >/dev/null 2>&1; do sleep 1; done; ollama pull {defaultEmbeddingModel}; ollama pull {defaultLlmModel}; kill $ollama_pid; wait $ollama_pid")
    .WaitFor(ollama);

// Whisper audio-to-text runtime (issue #210). Optional: api/worker do NOT WaitFor this so
// captioned ingestion proceeds with a warning when whisper is absent (PRD §2.4).
// TODO(model-download-implementation-plan): swap placeholder image for verified image.
var whisper = builder.AddContainer("whisper", whisperImage)
    .WithImageTag(whisperImageTag)
    .WithHttpEndpoint(targetPort: whisperPort, port: whisperPort, name: "http")
    .WithHttpHealthCheck("/health");

var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib")
    .WithImageTag("0.114.0")
    .WithBindMount("../../compose/observability/otel-collector.yaml", "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
    .WithEndpoint(targetPort: 4317, port: 4317, name: "grpc", isExternal: true)
    .WithHttpEndpoint(targetPort: 4318, port: 4318);

var prometheus = builder.AddContainer("prometheus", "prom/prometheus")
    .WithImageTag("v2.54.0")
    .WithVolume("streamingdigest-prometheus-data", "/prometheus")
    .WithBindMount("../../compose/observability/prometheus.yml", "/etc/prometheus/prometheus.yml", isReadOnly: true)
    .WithArgs("--config.file=/etc/prometheus/prometheus.yml")
    .WithHttpEndpoint(targetPort: 9090, port: 9090);

var grafana = builder.AddContainer("grafana", "grafana/grafana")
    .WithImageTag("11.4.0")
    .WithEnvironment("GF_SECURITY_ADMIN_USER", grafanaAdminUser)
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", grafanaAdminPassword)
    .WithEnvironment("GF_SERVER_ROOT_URL", "%(protocol)s://%(domain)s:%(http_port)s/grafana/")
    .WithEnvironment("GF_SERVER_SERVE_FROM_SUB_PATH", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Viewer")
    .WithVolume("streamingdigest-grafana-data", "/var/lib/grafana")
    .WithBindMount("../../compose/observability/grafana/provisioning", "/etc/grafana/provisioning", isReadOnly: true)
    .WithBindMount("../../compose/observability/grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 3000, port: 3000)
    .WaitFor(prometheus);

var pgadmin = builder.AddContainer("pgadmin", "dpage/pgadmin4")
    .WithImageTag("9.6.0")
    .WithEnvironment("PGADMIN_DEFAULT_EMAIL", pgAdminDefaultEmail)
    .WithEnvironment("PGADMIN_DEFAULT_PASSWORD", pgAdminDefaultPassword)
    .WithEnvironment("PGADMIN_LISTEN_PORT", "5050")
    .WithVolume("streamingdigest-pgadmin-data", "/var/lib/pgadmin")
    .WithHttpEndpoint(targetPort: 5050, port: 5050)
    .WaitFor(postgres);

var loki = builder.AddContainer("loki", "grafana/loki")
    .WithImageTag("3.2.0")
    .WithVolume("streamingdigest-loki-data", "/loki")
    .WithBindMount("../../compose/observability/loki-config.yaml", "/etc/loki/local-config.yaml", isReadOnly: true)
    .WithArgs("-config.file=/etc/loki/local-config.yaml")
    .WithHttpEndpoint(targetPort: 3100, port: 3100);

var tempo = builder.AddContainer("tempo", "grafana/tempo")
    .WithImageTag("2.6.0")
    .WithVolume("streamingdigest-tempo-data", "/var/tempo")
    .WithBindMount("../../compose/observability/tempo.yaml", "/etc/tempo.yaml", isReadOnly: true)
    .WithArgs("-config.file=/etc/tempo.yaml")
    .WithHttpEndpoint(targetPort: 3200, port: 3200);

var scraper = builder.AddDockerfile("scraper", "../StreamingDigest.Scraper")
    .WithEnvironment("NODE_ENV", "production")
    .WithEnvironment("PORT", "3000")
    .WithHttpEndpoint(env: "PORT", targetPort: 3000)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.StreamingDigest_Api>("api")
    .WithExternalHttpEndpoints()
    .WithReference(postgres)
    .WaitFor(postgres)
    .WaitFor(scraper)
    .WaitFor(ollama)
    .WaitForCompletion(ollamaBootstrap)
    .WaitFor(otelCollector)
    .WaitFor(pgadmin)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317")
    .WithEnvironment("STREAMINGDIGEST_OBSERVABILITY_ENABLED", "true")
    .WithEnvironment("STREAMINGDIGEST_EMBEDDING_MODEL", defaultEmbeddingModel)
    .WithEnvironment("STREAMINGDIGEST_LLM_MODEL", defaultLlmModel)
    .WithEnvironment("Scraper__BaseUrl", "http://scraper:3000")
    .WithEnvironment("llm__baseUrl", "http://ollama:11434")
    .WithEnvironment("STREAMINGDIGEST_WHISPER_BASE_URL", "http://whisper:" + whisperPort.ToString(CultureInfo.InvariantCulture))
    .WithEnvironment("observability:services:grafana:url", "http://grafana:3000")
    .WithEnvironment("observability:services:pgadmin:url", "http://pgadmin:5050")
    .WithEnvironment("observability:services:prometheus:url", "http://prometheus:9090")
    .WithEnvironment("observability:services:loki:url", "http://loki:3100")
    .WithEnvironment("observability:services:tempo:url", "http://tempo:3200")
    .WithEnvironment("observability:services:otelCollector:url", "http://otel-collector:4317");

builder.AddProject<Projects.StreamingDigest_Worker>("worker")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WaitFor(scraper)
    .WaitFor(ollama)
    .WaitForCompletion(ollamaBootstrap)
    .WaitFor(otelCollector)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317")
    .WithEnvironment("STREAMINGDIGEST_OBSERVABILITY_ENABLED", "true")
    .WithEnvironment("STREAMINGDIGEST_EMBEDDING_MODEL", defaultEmbeddingModel)
    .WithEnvironment("STREAMINGDIGEST_LLM_MODEL", defaultLlmModel)
    .WithEnvironment("Scraper__BaseUrl", "http://scraper:3000")
    .WithEnvironment("llm__baseUrl", "http://ollama:11434")
    .WithEnvironment("STREAMINGDIGEST_WHISPER_BASE_URL", "http://whisper:" + whisperPort.ToString(CultureInfo.InvariantCulture));

builder.Build().Run();

static void SetEnvDefault(IDictionary<string, CapturedEnvironmentVariable> env, string key, string defaultValue)
{
    if (env.TryGetValue(key, out var variable))
    {
        variable.DefaultValue = defaultValue;
    }
}

static void SetContainerName(ComposeFile composeFile, string serviceName, string containerName)
{
    if (composeFile.Services.TryGetValue(serviceName, out var service))
    {
        service.ContainerName = containerName;
    }
}

static void SetPublishedPorts(ComposeFile composeFile, string serviceName, List<string> ports)
{
    if (composeFile.Services.TryGetValue(serviceName, out var service))
    {
        service.Ports = ports;
    }
}
