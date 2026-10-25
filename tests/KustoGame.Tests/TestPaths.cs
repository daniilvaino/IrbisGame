namespace KustoGame.Tests;

public static class TestPaths
{
    /// <summary>Repository root: the first parent folder of the test binaries that contains KustoGame.slnx.</summary>
    public static string Root { get; } = FindRoot();

    public static string Levels => Path.Combine(Root, "levels");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "KustoGame.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException("Не найден корень репозитория (KustoGame.slnx).");
    }
}
