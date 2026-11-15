using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.EventLog;

/// <summary>
/// The event log (UI.md 3.8): chronological rows, a category filter, a case-insensitive search over text, source,
/// category and MET, and the pinned unacknowledged critical alarms above it. Changing the filter or search rebuilds
/// <see cref="Rows"/> once; appends insert only matching rows.
/// </summary>
public sealed class EventLogViewModel : ViewModelBase
{
    public const int DefaultCapacity = 5000;

    private readonly int _capacity;
    private readonly ConsoleWords _words;
    private readonly List<LogRow> _all = [];
    private readonly ObservableList<LogRow> _rows = new();
    private readonly ObservableValue<LogFilter> _filter = new(LogFilter.All);
    private readonly ObservableValue<string> _search = new(string.Empty);
    private readonly ObservableValue<int> _totalCount = new();
    private readonly List<LogRow> _scratch = [];

    /// <param name="alarms">Source of the pinned alarm rows; none are pinned when null.</param>
    /// <param name="capacity">Oldest rows are dropped beyond this many.</param>
    public EventLogViewModel(ConsoleWords words, AlarmListViewModel? alarms = null, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _words = words;
        _capacity = capacity;
        Pinned = alarms?.Pinned ?? new ObservableList<AlarmRow>();
    }

    /// <summary>Rows passing the filter and search, oldest first.</summary>
    public ObservableList<LogRow> Rows => _rows;

    /// <summary>Unacknowledged critical alarms pinned at the top.</summary>
    public ObservableList<AlarmRow> Pinned { get; }

    public IReadOnlyObservable<LogFilter> Filter => _filter;

    public IReadOnlyObservable<string> Search => _search;

    /// <summary>Rows held, before filtering.</summary>
    public IReadOnlyObservable<int> TotalCount => _totalCount;

    public void SetFilter(LogFilter filter)
    {
        if (_filter.Set(filter)) Rebuild();
    }

    public void SetSearch(string? search)
    {
        if (_search.Set(search?.Trim() ?? string.Empty)) Rebuild();
    }

    public void ClearSearch() => SetSearch(null);

    public void Append(LogEntry entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var row = LogRow.From(entry, _words);
        _all.Insert(InsertionIndex(_all, row), row);
        if (IsMatch(row, _filter.Value, _search.Value)) _rows.Insert(InsertionIndex(_rows, row), row);
        TrimToCapacity();
        _totalCount.Value = _all.Count;
    }

    public void AppendRange(IReadOnlyList<LogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries) Append(entry);
    }

    public static bool IsMatch(LogRow row, LogFilter filter, string search)
    {
        ArgumentNullException.ThrowIfNull(row);
        var categoryMatches = filter switch
        {
            LogFilter.All => true,
            LogFilter.Alarms => row.Category == LogCategory.Alarm,
            LogFilter.Crew => row.Category == LogCategory.Crew,
            LogFilter.Commands => row.Category == LogCategory.Command,
            LogFilter.SystemChanges => row.Category == LogCategory.SystemChange,
            LogFilter.Decisions => row.Category == LogCategory.Decision,
            _ => false,
        };
        if (!categoryMatches) return false;
        if (string.IsNullOrEmpty(search)) return true;
        return row.Text.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Source.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.CategoryWord.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.MetText.Contains(search, StringComparison.Ordinal);
    }

    private void Rebuild()
    {
        _scratch.Clear();
        foreach (var row in _all)
        {
            if (IsMatch(row, _filter.Value, _search.Value)) _scratch.Add(row);
        }
        _rows.ResetTo(_scratch);
    }

    private void TrimToCapacity()
    {
        while (_all.Count > _capacity)
        {
            var oldest = _all[0];
            _all.RemoveAt(0);
            if (_rows.Count > 0 && _rows[0] == oldest) _rows.RemoveAt(0);
        }
    }

    // Entries normally arrive in order, so this scans from the end and stops at once.
    private static int InsertionIndex(IReadOnlyList<LogRow> rows, LogRow row)
    {
        var index = rows.Count;
        while (index > 0 && Compare(rows[index - 1], row) > 0) index--;
        return index;
    }

    private static int Compare(LogRow a, LogRow b)
    {
        var order = a.At.CompareTo(b.At);
        return order != 0 ? order : a.Sequence.CompareTo(b.Sequence);
    }
}
