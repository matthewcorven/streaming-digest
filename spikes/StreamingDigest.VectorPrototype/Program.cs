// ============================================================
// THROWAWAY PROTOTYPE — dispatcher.
//   dotnet run --project spikes/StreamingDigest.VectorPrototype -- 11.3a   (original, issue #18)
//   dotnet run --project spikes/StreamingDigest.VectorPrototype -- query   (11.3b, issue #19)
// Requires ConnectionStrings__streamingdigest from the running Aspire
// AppHost postgres resource. ZERO external AI calls.
// ============================================================

using Npgsql;
using VectorPrototype;

// Map pgvector's `vector` type globally so every connection (main + reader) can read/write it.
NpgsqlConnection.GlobalTypeMapper.UseVector();

var mode = args.Length > 0 ? args[0] : "query";
var connString = Environment.GetEnvironmentVariable("ConnectionStrings__streamingdigest")
    ?? throw new InvalidOperationException("Set ConnectionStrings__streamingdigest (from the running Aspire AppHost postgres resource).");

switch (mode)
{
    case "11.3a":
        await Runner113a.RunAsync(connString);
        break;
    case "query":
    case "11.3b":
        await Runner113b.RunAsync(connString);
        break;
    default:
        Console.Error.WriteLine($"unknown mode '{mode}'. Use '11.3a' or 'query'.");
        return 1;
}
return 0;
