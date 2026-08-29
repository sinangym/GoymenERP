using System.Text.Json;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyIdentitySeedJsonStrictnessTests
{
    private const string IdentitySeedPath =
        "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void Identity_seed_is_strict_json_without_duplicate_properties()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            IdentitySeedPath.Replace('/', Path.DirectorySeparatorChar));
        var bytes = File.ReadAllBytes(path);

        using var document = JsonDocument.Parse(
            bytes,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
        var errors = new List<string>();
        FindDuplicateProperties(document.RootElement, "$", errors);

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    private static void FindDuplicateProperties(
        JsonElement element,
        string location,
        ICollection<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    errors.Add(
                        $"Duplicate identity-seed property at {location}: {property.Name}.");
                }

                FindDuplicateProperties(
                    property.Value,
                    $"{location}.{property.Name}",
                    errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindDuplicateProperties(item, $"{location}[{index}]", errors);
                index++;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Göymen ERP repository root was not found.");
    }
}
