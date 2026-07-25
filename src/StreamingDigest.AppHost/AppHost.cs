using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    // pgvector is required (ARCHITECTURE.md target runtime: "PostgreSQL + pgvector").
    // The pgvector image bundles the `vector` extension on top of stock postgres.
    .WithImage("pgvector/pgvector", "pg17")
    .WithImageRegistry("docker.io")
    .WithDataVolume("streamingdigest-postgres-data")
    .AddDatabase("streamingdigest");

builder.AddProject<Projects.StreamingDigest_Api>("api")
    .WithReference(postgres)
    .WaitFor(postgres);

builder.AddProject<Projects.StreamingDigest_Worker>("worker")
    .WithReference(postgres)
    .WaitFor(postgres);

builder.Build().Run();
