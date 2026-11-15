using Godot;
using Usa.Game.Content;
using Usa.Game.Presentation.Workspace;
using Usa.Game.UI.Console;
using Usa.Game.UI.Entry;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Workspace;

/// <summary>
/// The Assign step (<c>res://ui/workspace/AssigneePicker.tscn</c>, 320 wide; Figma G5-01b node 37:2219): requirements, one
/// option per astronaut and "Assign to …" / Cancel. It only picks; the workspace calls <see cref="WorkspaceModel.Assign"/>.
/// </summary>
public sealed partial class AssigneePickerView : PanelContainer
{
    private static readonly TextSlot[] Slots =
    [
        new("%Aside", "workspace.pickerAside"),
        new("%CancelButton", "workspace.pickerCancel"),
    ];

    private Label _title = null!;
    private Label _requirements = null!;
    private Label _note = null!;
    private VBoxContainer _options = null!;
    private Button _assign = null!;
    private Button _cancel = null!;
    private readonly ButtonGroup _group = new();
    private CrewId? _selected;
    private string? _selectedName;
    private WorkspaceWords _words = WorkspaceWords.Design;
    private ConsolePalette _palette = ConsolePalette.Default;

    /// <summary>The player confirmed an astronaut.</summary>
    public event Action<CrewId>? Confirmed;

    public event Action? Cancelled;

    /// <summary><c>AssigneeOption.tscn</c>, set in the scene.</summary>
    [Export]
    public PackedScene? OptionScene { get; set; }

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _requirements = GetNode<Label>("%Requirements");
        _note = GetNode<Label>("%Note");
        _options = GetNode<VBoxContainer>("%Options");
        _assign = GetNode<Button>("%AssignButton");
        _cancel = GetNode<Button>("%CancelButton");
        _note.Text = _words.PickerNote;
        _assign.Pressed += () =>
        {
            if (_selected is { } crew) Confirmed?.Invoke(crew);
        };
        _cancel.Pressed += () => Cancelled?.Invoke();
    }

    /// <summary>The picker's words (<c>workspace.picker*</c>), once per mission.</summary>
    public void ApplyText(ITextSource text, WorkspaceWords words)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(words);
        _words = words;
        if (!IsNodeReady()) return;
        SceneText.Apply(this, text, Slots);
        _note.Text = words.PickerNote;
        UpdateAssignButton();
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
    }

    public void Show(AssignmentPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        _title.Text = WorkspaceWords.Fill(_words.PickerTitleNamed, ("goal", preview.GoalName.ToUpperInvariant()));
        _requirements.Text = preview.RequirementsText;

        ChildSync.Sync(_options, preview.Options.Count, OptionScene);
        _selected = null;
        _selectedName = null;
        for (var i = 0; i < preview.Options.Count; i++)
        {
            var option = preview.Options[i];
            var view = _options.GetChild<AssigneeOptionView>(i);
            view.ButtonGroup = _group;
            view.Show(option, _words, _palette);
            view.SetPressedNoSignal(false);
            view.Pressed -= OnOptionPressed;
            view.Pressed += OnOptionPressed;
            if (_selected is null && option.Available && option.Likelihood == LikelihoodBand.High) Select(view);
        }
        if (_selected is null)
        {
            foreach (var child in _options.GetChildren())
            {
                if (child is AssigneeOptionView { Disabled: false } first)
                {
                    Select(first);
                    break;
                }
            }
        }
        UpdateAssignButton();
        Visible = true;
    }

    private void OnOptionPressed()
    {
        if (_group.GetPressedButton() is AssigneeOptionView view) Select(view);
        UpdateAssignButton();
    }

    private void Select(AssigneeOptionView view)
    {
        view.SetPressedNoSignal(true);
        _selected = view.Option?.Crew;
        _selectedName = view.Option?.Name;
    }

    private void UpdateAssignButton()
    {
        _assign.Disabled = _selected is null;
        var first = _selectedName?.Split(' ')[^1];
        _assign.Text = first is null ? _words.PickerAssign : WorkspaceWords.Fill(_words.PickerAssignTo, ("name", first));
    }
}
