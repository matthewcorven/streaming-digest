using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var postgresUsername = builder.AddParameter("postgres-username");
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", postgresUsername, postgresPassword)
    // pgvector is required (ARCHITECTURE.md target runtime: "PostgreSQL + pgvector").
    // The pgvector image bundles the `vector` extension on top of stock postgres.
    .WithImage("pgvector/pgvector")
    .WithImageTag("0.8.5-pg18-trixie")
    .WithImageRegistry("docker.io")
    .WithVolume("streamingdigest-postgres18-data", "/var/lib/postgresql")
    .AddDatabase("streamingdigest");

builder.AddProject<Projects.StreamingDigest_Api>("api")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317")
    .WithEnvironment("STREAMINGDIGEST_OBSERVABILITY_ENABLED", "true")
    .WithEnvironment("observability:services:grafana:url", "http://grafana:3000")
    .WithEnvironment("observability:services:prometheus:url", "http://prometheus:9090")
    .WithEnvironment("observability:services:loki:url", "http://loki:3100")
    .WithEnvironment("observability:services:tempo:url", "http://tempo:3200")
    .WithEnvironment("observability:services:otelCollector:url", "http://otel-collector:4317");

builder.AddProject<Projects.StreamingDigest_Worker>("worker")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317")
    .WithEnvironment("STREAMINGDIGEST_OBSERVABILITY_ENABLED", "true");

builder.AddJavaScriptApp("scraper", "../StreamingDigest.Scraper")
    .WithRunScript("start");

builder.Build().Run();
