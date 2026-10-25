using System.Reflection;
using System.Runtime.Loader;

namespace KustoGame.Server.Engines;

/// <summary>
/// Loads an engine adapter from engines/&lt;name&gt;/ into its own <see cref="AssemblyLoadContext"/>,
/// so that it gets its own copy of Kusto.Language. The contracts assembly and System.Text.Json
/// are shared with the host, otherwise <see cref="IKqlEngine"/> and JsonNode values would not cross the boundary.
/// </summary>
public static class IsolatedEngineLoader
{
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        typeof(IKqlEngine).Assembly.GetName().Name!,
        "System.Text.Json",
        "System.Text.Encodings.Web",
    };

    public static IKqlEngine Load(string engineName, string assemblyName, string typeName)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "engines", engineName);
        var path = Path.Combine(dir, assemblyName + ".dll");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Не найдена сборка движка {engineName}: {path}. Соберите решение целиком (dotnet build).", path);

        var context = new EngineLoadContext(engineName, dir);
        var assembly = context.LoadFromAssemblyPath(path);
        var type = assembly.GetType(typeName, throwOnError: true)!;
        return (IKqlEngine)Activator.CreateInstance(type)!;
    }

    private sealed class EngineLoadContext(string name, string directory) : AssemblyLoadContext($"engine:{name}")
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null || Shared.Contains(assemblyName.Name)) return null;
            var candidate = Path.Combine(directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}
