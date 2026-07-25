using StreamingDigest.Application.Configuration;

namespace StreamingDigest.UnitTests;

public class ApplicationConfigurationLoaderTests
{
    [Fact]
    public void LoadFromDirectory_ValidConfiguration_ReturnsParsedConfiguration()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            WriteConfigFiles(tempDirectory, """
            {
              "configSchemaVersion": "1.0.0",
              "app": {
                "name": "streaming-digest",
                "environment": "Development",
                "mutableSettingsStore": "file"
              },
              "runtime": {
                "enableHttpRedirect": true,
                "defaultTheme": "system",
                "paginationPageSize": 25
              },
              "connectionStrings": {
                "streamingdigest": "Host=localhost;Port=5432;Database=streamingdigest;Username=postgres;Password=postgres"
              },
              "logging": {
                "level": "Information"
              }
            }
            """);

            var configuration = ApplicationConfigurationLoader.LoadFromDirectory(tempDirectory);

            Assert.Equal("1.0.0", configuration.ConfigSchemaVersion);
            Assert.Equal("streaming-digest", configuration.App.Name);
            Assert.Equal("file", configuration.App.MutableSettingsStore);
            Assert.Equal("Information", configuration.Logging.Level);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void LoadFromDirectory_InvalidConfiguration_ThrowsHelpfulValidationError()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            WriteConfigFiles(tempDirectory, """
            {
              "configSchemaVersion": "1.0.0",
              "app": {
                "name": "streaming-digest",
                "environment": "Development",
                "mutableSettingsStore": "file"
              },
              "runtime": {
                "enableHttpRedirect": true,
                "defaultTheme": "invalid-theme",
                "paginationPageSize": 25
              },
              "connectionStrings": {
                "streamingdigest": "Host=localhost;Port=5432;Database=streamingdigest;Username=postgres;Password=postgres"
              },
              "logging": {
                "level": "Information"
              }
            }
            """);

            var exception = Assert.Throws<InvalidOperationException>(() => ApplicationConfigurationLoader.LoadFromDirectory(tempDirectory));

            Assert.Contains("validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"streaming-digest-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void WriteConfigFiles(string directory, string configJson)
    {
        File.WriteAllText(Path.Combine(directory, "appsettings.json"), configJson);
        File.WriteAllText(Path.Combine(directory, "appsettings.schema.json"), """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["configSchemaVersion", "app", "runtime", "connectionStrings", "logging"],
          "properties": {
            "configSchemaVersion": {
              "type": "string",
              "pattern": "^\\d+\\.\\d+\\.\\d+$"
            },
            "app": {
              "type": "object",
              "required": ["name", "environment", "mutableSettingsStore"],
              "properties": {
                "name": {
                  "type": "string",
                  "minLength": 1
                },
                "environment": {
                  "type": "string",
                  "enum": ["Development", "Staging", "Production"]
                },
                "mutableSettingsStore": {
                  "type": "string",
                  "enum": ["file", "database"]
                }
              },
              "additionalProperties": false
            },
            "runtime": {
              "type": "object",
              "required": ["enableHttpRedirect", "defaultTheme", "paginationPageSize"],
              "properties": {
                "enableHttpRedirect": {
                  "type": "boolean"
                },
                "defaultTheme": {
                  "type": "string",
                  "enum": ["light", "dark", "system"]
                },
                "paginationPageSize": {
                  "type": "integer",
                  "minimum": 10,
                  "maximum": 200
                }
              },
              "additionalProperties": false
            },
            "connectionStrings": {
              "type": "object",
              "required": ["streamingdigest"],
              "properties": {
                "streamingdigest": {
                  "type": "string",
                  "minLength": 1
                }
              },
              "additionalProperties": false
            },
            "logging": {
              "type": "object",
              "required": ["level"],
              "properties": {
                "level": {
                  "type": "string",
                  "enum": ["Trace", "Debug", "Information", "Warning", "Error", "Critical"]
                }
              },
              "additionalProperties": false
            }
          },
          "additionalProperties": true
        }
        """);
    }
}
