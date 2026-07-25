using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("streamingdigest-postgres-data")
    .AddDatabase("streamingdigest");

builder.AddProject<Projects.StreamingDigest_Api>("api")
    .WithReference(postgres)
    .WaitFor(postgres);

builder.AddProject<Projects.StreamingDigest_Worker>("worker")
    .WithReference(postgres)
    .WaitFor(postgres);

builder.Build().Run();
