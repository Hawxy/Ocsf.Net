namespace Ocsf.Generator;

internal static class Program
{
    private const string DefaultVersion = "1.9.0";

    internal static async Task<int> Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["fetch", .. var rest] => await FetchAsync(GetOption(rest, "--version") ?? DefaultVersion),
                ["generate", .. var rest] => Generate(
                    GetOption(rest, "--schema") ?? DefaultSchemaPath(),
                    GetOption(rest, "--repo") ?? RepoRoot()),
                ["verify", .. var rest] => Verify(
                    GetOption(rest, "--schema") ?? DefaultSchemaPath(),
                    GetOption(rest, "--repo") ?? RepoRoot()),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: ocsf-generator <command> [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Commands:");
        Console.Error.WriteLine("  fetch    [--version 1.9.0]        Download the schema export to schema/");
        Console.Error.WriteLine("  generate [--schema path]          Regenerate all generated code");
        Console.Error.WriteLine("  verify   [--schema path]          Fail if generated code is out of date");
        return 2;
    }

    private static string? GetOption(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Walks up from the current directory to the repository root (marked by the solution file).</summary>
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (; dir is not null; dir = dir.Parent)
        {
            if (dir.EnumerateFiles("ocsf.net.slnx").Any())
                return dir.FullName;
        }
        throw new InvalidOperationException("Repository root not found (no ocsf.net.slnx in any parent directory).");
    }

    private static string DefaultSchemaPath() =>
        Path.Combine(RepoRoot(), "schema", $"ocsf-schema-{DefaultVersion}.json");

    private static async Task<int> FetchAsync(string version)
    {
        var url = SchemaLoader.BuildExportUrl(version);
        var target = Path.Combine(RepoRoot(), "schema", $"ocsf-schema-{version}.json");
        Console.WriteLine($"Fetching {url}");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var json = await http.GetStringAsync(url);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Normalize to LF and ensure a trailing newline for stable git diffs.
        await File.WriteAllTextAsync(target, json.Replace("\r\n", "\n").TrimEnd('\n') + "\n");
        Console.WriteLine($"Wrote {target} ({new FileInfo(target).Length:N0} bytes)");

        var schema = SchemaLoader.Load(target);
        Console.WriteLine($"Parsed OK: version {schema.Version}, {schema.Classes.Count} classes, " +
                          $"{schema.Objects.Count} objects, {schema.Types.Count} types.");
        return 0;
    }

    private static int Generate(string schemaPath, string repoRoot)
    {
        var schema = SchemaLoader.Load(schemaPath);
        var outputs = Emitter.EmitAll(schema);
        var written = 0;

        foreach (var root in Emitter.OutputRoots)
        {
            var dir = Path.Combine(repoRoot, root);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }

        foreach (var (relativePath, content) in outputs)
        {
            var path = Path.Combine(repoRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            written++;
        }

        Console.WriteLine($"Generated {written} files from schema {schema.Version}.");
        return 0;
    }

    private static int Verify(string schemaPath, string repoRoot)
    {
        var schema = SchemaLoader.Load(schemaPath);
        var outputs = Emitter.EmitAll(schema);
        var stale = new List<string>();

        var expected = outputs.ToDictionary(o => o.RelativePath.Replace('\\', '/'), o => o.Content);
        foreach (var (relativePath, content) in expected)
        {
            var path = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(path) || File.ReadAllText(path) != content)
                stale.Add(relativePath);
        }

        // Files on disk that the generator no longer produces are also drift.
        foreach (var root in Emitter.OutputRoots)
        {
            var dir = Path.Combine(repoRoot, root);
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.g.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (!expected.ContainsKey(rel))
                    stale.Add(rel + " (orphaned)");
            }
        }

        if (stale.Count > 0)
        {
            Console.Error.WriteLine("Generated code is out of date. Run: dotnet run --project src/Ocsf.Generator -- generate");
            foreach (var s in stale.Order(StringComparer.Ordinal))
                Console.Error.WriteLine($"  {s}");
            return 1;
        }

        Console.WriteLine($"Generated code is up to date ({outputs.Count} files).");
        return 0;
    }
}
