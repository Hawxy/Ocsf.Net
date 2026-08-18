namespace Ocsf.Generator.Tests;

internal static class TestPaths
{
    internal static string RepoRoot { get; } = FindRepoRoot();

    internal static string SchemaFile => Path.Combine(RepoRoot, "schema", "ocsf-schema-1.9.0.json");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (; dir is not null; dir = dir.Parent)
        {
            if (dir.EnumerateFiles("Ocsf.Net.slnx").Any())
                return dir.FullName;
        }
        throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
