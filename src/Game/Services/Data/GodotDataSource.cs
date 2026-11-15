using System.IO.Enumeration;
using Godot;
using Usa.Simulation.Data;

namespace Usa.Game.Services.Data;

/// <summary>
/// <see cref="IDataSource"/> over Godot's virtual file system (<c>res://data</c> by default), so the same loaders
/// read loose files in the editor and the packed files in an export. Paths are relative with '/' separators.
/// Export presets must include <c>data/*.json</c> as non-resource files.
/// </summary>
public sealed class GodotDataSource : IDataSource
{
    public const string DefaultRoot = "res://data";

    // Exports list imported resources as "<name>.remap"; data files are not imported, but strip it defensively.
    private const string RemapSuffix = ".remap";

    public GodotDataSource(string root = DefaultRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        Root = root.TrimEnd('/');
    }

    public string Root { get; }

    public bool Exists(string relativePath) => Godot.FileAccess.FileExists(Resolve(relativePath));

    public string ReadText(string relativePath)
    {
        var path = Resolve(relativePath);
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            var error = Godot.FileAccess.GetOpenError();
            throw new DefinitionException(relativePath, "$", error == Error.FileNotFound ? "File not found." : $"Cannot open file ({error}).");
        }
        return file.GetAsText();
    }

    public IReadOnlyList<string> List(string relativeDirectory, string searchPattern = "*.json")
    {
        var directory = Resolve(relativeDirectory);
        if (!DirAccess.DirExistsAbsolute(directory)) return [];

        var prefix = NormalizeRelative(relativeDirectory);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in DirAccess.GetFilesAt(directory))
        {
            var name = raw.EndsWith(RemapSuffix, StringComparison.Ordinal) ? raw[..^RemapSuffix.Length] : raw;
            if (FileSystemName.MatchesSimpleExpression(searchPattern, name, ignoreCase: true))
                names.Add(prefix.Length == 0 ? name : $"{prefix}/{name}");
        }
        return [.. names];
    }

    private string Resolve(string relativePath)
    {
        var relative = NormalizeRelative(relativePath);
        return relative.Length == 0 ? Root : $"{Root}/{relative}";
    }

    private static string NormalizeRelative(string relativePath) => relativePath.Replace('\\', '/').Trim('/');
}
