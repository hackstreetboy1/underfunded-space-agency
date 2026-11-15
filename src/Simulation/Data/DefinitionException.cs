namespace Usa.Simulation.Data;

/// <summary>Invalid data: always names the file and the JSON path so it can be fixed without a debugger.</summary>
public sealed class DefinitionException : Exception
{
    public DefinitionException(string file, string jsonPath, string message, Exception? inner = null)
        : base($"{file} {jsonPath}: {message}", inner)
    {
        File = file;
        JsonPath = jsonPath;
    }

    public DefinitionException(IReadOnlyList<DefinitionError> errors)
        : base(string.Join(Environment.NewLine, errors.Select(e => e.ToString())))
    {
        File = errors.Count > 0 ? errors[0].File : "";
        JsonPath = errors.Count > 0 ? errors[0].JsonPath : "";
        Errors = errors;
    }

    public string File { get; }

    public string JsonPath { get; }

    public IReadOnlyList<DefinitionError> Errors { get; } = [];
}

public sealed record DefinitionError(string File, string JsonPath, string Message)
{
    public override string ToString() => $"{File} {JsonPath}: {Message}";
}

/// <summary>Collects every cross-reference problem before failing, so one load reports all of them.</summary>
public sealed class DefinitionErrors
{
    private readonly List<DefinitionError> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public IReadOnlyList<DefinitionError> All => _errors;

    public void Add(string file, string jsonPath, string message) => _errors.Add(new DefinitionError(file, jsonPath, message));

    public void ThrowIfAny()
    {
        if (_errors.Count > 0) throw new DefinitionException(_errors.ToArray());
    }
}
