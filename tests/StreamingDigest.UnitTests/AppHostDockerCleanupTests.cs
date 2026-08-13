using System;
using System.IO;
using Xunit;

namespace StreamingDigest.UnitTests;

public class AppHostDockerCleanupTests
{
    [Fact]
    public void ScriptPath_Resolves_To_Repository_Root_Script_Directory()
    {
        var repoRoot = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repoRoot, "StreamingDigest.slnx")))
        {
            var parent = Directory.GetParent(repoRoot);
            if (parent is null)
            {
                throw new DirectoryNotFoundException("Could not find repository root from test working directory.");
            }

            repoRoot = parent.FullName;
        }

        var scriptPath = Path.Combine(repoRoot, "scripts", "prune_orphaned_scrapers.sh");
        Assert.True(File.Exists(scriptPath), $"Expected cleanup script at {scriptPath}.");

        var powershellScriptPath = Path.Combine(repoRoot, "scripts", "prune_orphaned_scrapers.ps1");
        Assert.True(File.Exists(powershellScriptPath), $"Expected cleanup script at {powershellScriptPath}.");
    }
}
