using System.Text;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>Named argument values for a parameterized task, kept in ordinal name order.</summary>
public sealed class TaskArguments
{
    private readonly KeyValuePair<string, StateValue>[] _entries;

    public TaskArguments(IEnumerable<KeyValuePair<string, StateValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToArray();
        Array.Sort(_entries, (a, b) => string.CompareOrdinal(a.Key, b.Key));
        for (var i = 1; i < _entries.Length; i++)
        {
            if (_entries[i].Key == _entries[i - 1].Key)
                throw new ArgumentException($"Argument '{_entries[i].Key}' is given twice.", nameof(entries));
        }
    }

    public static TaskArguments Empty { get; } = new([]);

    public IReadOnlyList<KeyValuePair<string, StateValue>> Entries => _entries;

    public int Count => _entries.Length;

    public static TaskArguments Of(params (string Name, StateValue Value)[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var pairs = new KeyValuePair<string, StateValue>[entries.Length];
        for (var i = 0; i < entries.Length; i++) pairs[i] = new(entries[i].Name, entries[i].Value);
        return new TaskArguments(pairs);
    }

    public bool TryGet(string name, out StateValue value)
    {
        foreach (var entry in _entries)
        {
            if (entry.Key == name)
            {
                value = entry.Value;
                return true;
            }
        }
        value = StateValue.None;
        return false;
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var entry in _entries)
        {
            if (text.Length > 0) text.Append(", ");
            text.Append(entry.Key).Append('=').Append(TaskTemplate.Format(entry.Value));
        }
        return text.ToString();
    }
}
