using System.Text.Json;

namespace Usa.Simulation.Data;

/// <summary>Root records of data files carry a schema tag such as "vehicle/1".</summary>
public interface IVersionedDefinition
{
    string Schema { get; }
}

/// <summary>Where data files come from: the file system in tests, Godot's res:// in the game.</summary>
public interface IDataSource
{
    bool Exists(string relativePath);

    string ReadText(string relativePath);

    /// <summary>Relative paths of files directly inside <paramref name="relativeDirectory"/>, sorted ordinally.</summary>
    IReadOnlyList<string> List(string relativeDirectory, string searchPattern = "*.json");
}

public sealed class FileSystemDataSource(string rootDirectory) : IDataSource
{
    public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);

    public bool Exists(string relativePath) => File.Exists(Resolve(relativePath));

    public string ReadText(string relativePath)
    {
        var path = Resolve(relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : throw new DefinitionException(relativePath, "$", "File not found.");
    }

    public IReadOnlyList<string> List(string relativeDirectory, string searchPattern = "*.json")
    {
        var directory = Resolve(relativeDirectory);
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, searchPattern)
            .Select(file => Path.GetRelativePath(RootDirectory, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private string Resolve(string relativePath) => Path.Combine(RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>Deserializes data files with <see cref="DataJson.Options"/> and checks the schema tag.</summary>
public sealed class DefinitionLoader(IDataSource source)
{
    public IDataSource Source { get; } = source;

    public T Load<T>(string relativePath, string expectedSchema) where T : IVersionedDefinition
    {
        var definition = Deserialize<T>(relativePath, Source.ReadText(relativePath));
        if (definition.Schema != expectedSchema)
            throw new DefinitionException(relativePath, "$.schema", $"Expected schema '{expectedSchema}' but found '{definition.Schema}'.");
        return definition;
    }

    public static T Deserialize<T>(string fileLabel, string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, DataJson.Options)
                   ?? throw new DefinitionException(fileLabel, "$", "File is empty or null.");
        }
        catch (JsonException ex)
        {
            var location = ex.LineNumber is { } line ? $"{ex.Path ?? "$"} (line {line + 1})" : ex.Path ?? "$";
            throw new DefinitionException(fileLabel, location, ex.Message, ex);
        }
    }
}
