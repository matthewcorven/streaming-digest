using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StreamingDigest.UnitTests.Fixtures;

public sealed class FixtureLoader
{
    public string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Fixture path is required.", nameof(relativePath));
        }

        var candidates = new List<string>();

        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            candidates.Add(Path.Combine(currentDirectory.FullName, "tests", "Fixtures", relativePath));
            candidates.Add(Path.Combine(currentDirectory.FullName, "Fixtures", relativePath));
            currentDirectory = currentDirectory.Parent;
        }

        var currentWorkingDirectory = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(currentWorkingDirectory, "tests", "Fixtures", relativePath));
        candidates.Add(Path.Combine(currentWorkingDirectory, "Fixtures", relativePath));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Fixture '{relativePath}' could not be located.", relativePath);
    }

    public string ReadText(string relativePath)
    {
        return File.ReadAllText(Resolve(relativePath));
    }
}
