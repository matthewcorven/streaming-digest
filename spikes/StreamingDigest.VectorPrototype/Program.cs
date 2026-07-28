// ============================================================
// THROWAWAY PROTOTYPE — dispatcher.
//   dotnet run --project spikes/StreamingDigest.VectorPrototype -- 11.3a   (original, issue #18)
//   dotnet run --project spikes/StreamingDigest.VectorPrototype -- query   (11.3b, issue #19)
//   dotnet run --project spikes/StreamingDigest.VectorPrototype -- 12.x    (issue #100)
// 11.3a / 11.3b require ConnectionStrings__streamingdigest from the running Aspire
// AppHost postgres resource. 12.x talks directly to the real Ollama embedding provider.
// ============================================================

using Npgsql;
using VectorPrototype;

// Map pgvector's `vector` type globally so every connection (main + reader) can read/write it.
NpgsqlConnection.GlobalTypeMapper.UseVector();

var mode = args.Length > 0 ? args[0] : "query";

switch (mode)
{
    case "11.3a":
    {
        var connString = Environment.GetEnvironmentVariable("ConnectionStrings__streamingdigest")
            ?? throw new InvalidOperationException("Set ConnectionStrings__streamingdigest (from the running Aspire AppHost postgres resource).");
        await Runner113a.RunAsync(connString);
        break;
    }
    case "query":
    case "11.3b":
    {
        var connString = Environment.GetEnvironmentVariable("ConnectionStrings__streamingdigest")
            ?? throw new InvalidOperationException("Set ConnectionStrings__streamingdigest (from the running Aspire AppHost postgres resource).");
        await Runner113b.RunAsync(connString);
        break;
    }
    case "12.x":
    case "threshold":
    case "calibration":
        await Runner12xHighSignalThresholdCalibration.RunAsync();
        break;
    default:
        Console.Error.WriteLine($"unknown mode '{mode}'. Use '11.3a', 'query', or '12.x'.");
        return 1;
}
return 0;
