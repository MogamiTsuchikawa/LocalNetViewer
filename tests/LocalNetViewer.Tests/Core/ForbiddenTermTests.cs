namespace LocalNetViewer.Tests.Core;

public sealed class ForbiddenTermTests
{
    [Fact]
    public void SourceFiles_DoNotContainLegacyProductName()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var forbidden = string.Concat("Net", "Enum", "5");
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".csproj",
            ".sln",
            ".slnx",
            ".toml",
            ".json",
            ".axaml",
        };

        var files = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => extensions.Contains(Path.GetExtension(path)));

        var hits = files
            .Where(path => File.ReadAllText(path).Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(hits);
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LocalNetViewer.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
