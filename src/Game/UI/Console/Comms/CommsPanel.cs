using Godot;
using Usa.Game.Comms;
using Usa.Game.Comms.Hosting;
using Usa.Game.Input;
using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Console.Crew;
using Usa.Game.Services.Voice;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Binding.Comms;
using Usa.Game.UI.Console.Components;
using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Console.Comms;

/// <summary>
/// The comms panel (<c>CommsPanel.tscn</c>, Figma G1-03 node 13:364, G2-08; UI.md 3.5): searchable transcript, quick
/// commands, addressee selector, text input with send, the push-to-talk bar with its states and level meter, and the
/// "Undo (2 s)" chip after a voice order went out without review. Sends go through the <see cref="CommsController"/>, except
/// the unchanged text of an "Ask" / "Request…" pre-fill that maps to one action, which is sent as that request. The panel
/// never interprets anything itself.
/// </summary>
public sealed partial class CommsPanel : PanelContainer
{
    /// <summary>Quick commands shown before "More…" in the compact layout (Figma 21:807: one line).</summary>
    private const int CompactQuickCommands = 3;

    private readonly BindingScope _bindings = new();
    private readonly List<CrewId?> _addresseeIds = [];
    private readonly List<Button> _quickButtons = [];
    private readonly CommsInputModel _inputModel = new();
    private PanelHeader _header = null!;
    private LineEdit _search = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _messages = null!;
    private HFlowContainer _quickCommands = null!;
    private Button _more = null!;
    private Control _inputRow = null!;
    private OptionButton _addressee = null!;
    private LineEdit _input = null!;
    private Button _send = null!;
    private Button _undo = null!;
    private Button _ptt = null!;
    private ProgressBar _pttLevel = null!;
    private CommsController? _controller;
    private CrewPanelViewModel? _crew;
    private MicCapture? _mic;
    private TranscriptFeed? _feed;
    private Func<CommandDraft, ValidationResult>? _sendDraft;
    private bool _reviewing;
    private bool _followNewest = true;
    private bool _compact;
    private PttState _pttState = PttState.Idle;
    private ConsoleLabels? _labels;

    [Export]
    public PackedScene? RowScene { get; set; }

    [Export]
    public string YouWord { get; set; } = string.Empty;

    [Export]
    public string VoiceWord { get; set; } = string.Empty;

    [Export]
    public string CrewWord { get; set; } = string.Empty;

    [Export]
    public string ReportWord { get; set; } = string.Empty;

    [Export]
    public string RefusedWord { get; set; } = string.Empty;

    [Export]
    public string PttIdleText { get; set; } = string.Empty;

    [Export]
    public string PttListeningText { get; set; } = string.Empty;

    [Export]
    public string PttTranscribingText { get; set; } = string.Empty;

    [Export]
    public string PttReviewText { get; set; } = string.Empty;

    [Export]
    public string PttDisabledText { get; set; } = string.Empty;

    /// <summary>The addressee, text field and Send row (the guided first run's "ask the crew" anchor).</summary>
    public Control InputRow => _inputRow;

    /// <summary>The input's state: pending pre-fill and the voice undo chip.</summary>
    public CommsInputModel InputModel => _inputModel;

    public override void _Ready()
    {
        _header = GetNode<PanelHeader>("%Header");
        _search = GetNode<LineEdit>("%Search");
        _scroll = GetNode<ScrollContainer>("%Scroll");
        _messages = GetNode<VBoxContainer>("%Messages");
        _quickCommands = GetNode<HFlowContainer>("%QuickCommands");
        _more = GetNode<Button>("%More");
        _inputRow = GetNode<Control>("%InputRow");
        _addressee = GetNode<OptionButton>("%Addressee");
        _input = GetNode<LineEdit>("%Input");
        _send = GetNode<Button>("%Send");
        _undo = GetNode<Button>("%Undo");
        _ptt = GetNode<Button>("%Ptt");
        _pttLevel = GetNode<ProgressBar>("%PttLevel");

        _send.Pressed += SendInput;
        _input.TextSubmitted += _ => SendInput();
        _input.TextChanged += _inputModel.TextChanged;
        _undo.Pressed += UndoVoice;
        _more.Toggled += _ => ShowQuickCommands();
        _addressee.ItemSelected += index => SelectAddressee((int)index);
        _search.TextChanged += text => _feed?.SetSearch(text);
        _search.GuiInput += OnSearchInput;
        _ptt.ButtonDown += () => _mic?.BeginCapture();
        _ptt.ButtonUp += () => _mic?.EndCapture();
        var bar = _scroll.GetVScrollBar();
        bar.ValueChanged += _ => _followNewest = bar.Value + bar.Page >= bar.MaxValue - 1;
        bar.Changed += () =>
        {
            if (_followNewest) _scroll.ScrollVertical = (int)bar.MaxValue;
        };
        _search.Visible = false;
        _undo.Visible = false;
        ShowPtt(PttState.Idle);
        SetProcess(false);
    }

    public override void _ExitTree() => Unbind();

    // Presentation only: the PTT level meter while listening and the undo chip's real-time countdown.
    public override void _Process(double delta)
    {
        if (_mic is not null && _pttState == PttState.Listening) _pttLevel.Value = _mic.InputLevel * _pttLevel.MaxValue;
        _inputModel.Advance(delta);
        RefreshProcess();
    }

    /// <param name="mic">Push-to-talk capture for the PTT button; null leaves only the keyboard binding (handled by the mic node).</param>
    /// <param name="labels">Labels from the string table; null keeps the scene's design text.</param>
    /// <param name="sendDraft">Sends a pre-fill's suggested request as it is (the bridge's <c>Send(CommandDraft)</c>); null sends
    /// every input as text.</param>
    public void Bind(
        CommsController controller, CrewPanelViewModel crew, ConsolePalette palette, MicCapture? mic, ConsoleLabels? labels,
        Func<CommandDraft, ValidationResult>? sendDraft = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _controller = controller;
        _crew = crew;
        _mic = mic;
        _labels = labels;
        _sendDraft = sendDraft;
        if (labels is not null) ApplyLabels(labels);
        var words = new CommsWords(
            labels?.CommsYou ?? YouWord, labels?.CommsVoice ?? VoiceWord, labels?.AddresseeCrew ?? CrewWord,
            labels?.ReportBadge ?? ReportWord, labels?.CommsRefused ?? RefusedWord);
        var feed = new TranscriptFeed(controller.Comms.Transcript, words, CrewName);
        _feed = feed;
        _bindings.Add(feed);
        feed.VoiceSent += _inputModel.OfferUndo;
        if (RowScene is { } rowScene)
        {
            var actions = new TranscriptRowActions(
                requestId => controller.CancelPending(requestId),
                reply => SendAsPanel(() => controller.SendText(reply)),
                messageId => controller.AcceptOffer(messageId),
                text => FocusInput(text),
                labels?.EditResend)
            {
                CancelTooltip = labels?.CommsCancelRequest,
            };
            _bindings.Mirror(feed.Rows, new ListBinder<TranscriptRowModel, TranscriptRowView>(
                _messages, rowScene, (view, row) => view.Bind(row, palette, actions)));
        }
        _bindings.Observe(_inputModel.UndoSecondsLeft, seconds =>
        {
            _undo.Visible = seconds > 0;
            if (seconds > 0 && _labels is { } l) _undo.Text = l.PttUndo(seconds);
            RefreshProcess();
        });

        BuildQuickCommands(controller);
        BuildAddressees();
        _bindings.Add(crew.Cards.Subscribe(_ => BuildAddressees()));
        _bindings.On(crew.TalkRequested, TalkTo);

        controller.PttStateChanged += ShowPtt;
        controller.ReviewRequested += ShowReview;
        ShowPtt(controller.Comms.PttState);
    }

    public void Unbind()
    {
        if (_feed is { } feed) feed.VoiceSent -= _inputModel.OfferUndo;
        _bindings.Release();
        if (_controller is { } controller)
        {
            controller.PttStateChanged -= ShowPtt;
            controller.ReviewRequested -= ShowReview;
        }
        _controller = null;
        _crew = null;
        _mic = null;
        _labels = null;
        _feed = null;
        _sendDraft = null;
        _inputModel.ClearUndo();
        SetProcess(false);
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _header.Title = labels.CommsTitle;
        _header.Aside = labels.CommsSearchHint;
        _search.PlaceholderText = labels.CommsSearch;
        _addressee.TooltipText = labels.CommsAddresseeTooltip;
        _input.PlaceholderText = labels.CommsInput;
        _send.Text = labels.CommsSend;
        _more.Text = labels.CommsMore;
        PttReviewText = labels.PttReview;
        PttDisabledText = labels.PttDisabled;
    }

    /// <summary>Compact layout (Figma 21:807): quick commands on one line with a "More…" chip.</summary>
    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _compact = layout.Mode == ConsoleLayoutMode.Compact;
        if (!_compact) _more.SetPressedNoSignal(false);
        ShowQuickCommands();
    }

    /// <summary>
    /// "Ask" and "Request…": the text goes in the input for the player to review and send. Sent unchanged, a pre-fill that maps
    /// to one action goes out as that request; edited, it is free text.
    /// </summary>
    public void ApplyPrefill(CommsPrefill prefill)
    {
        ArgumentNullException.ThrowIfNull(prefill);
        _reviewing = false;
        FocusInput(prefill.Text);
        _inputModel.SetPrefill(prefill);
    }

    /// <summary><c>Enter</c> / <c>T</c>.</summary>
    public void FocusInput() => _input.GrabFocus();

    /// <summary><c>Ctrl+F</c>: opens the transcript search.</summary>
    public void FocusSearch()
    {
        _search.Visible = true;
        _search.GrabFocus();
    }

    private void SendInput()
    {
        if (_controller is not { } controller) return;
        var text = _input.Text.Trim();
        switch (_inputModel.Route(text, out var suggested))
        {
            case CommsSendRoute.None:
                return;
            case CommsSendRoute.Suggested when suggested is not null && _sendDraft is { } send:
                send(suggested);
                break;
            default:
                var reviewing = _reviewing;
                if (SendAsPanel(() => controller.SendText(text, reviewing)) is null) return;
                break;
        }
        _inputModel.Sent();
        _input.Clear();
        _reviewing = false;
    }

    private CommandId? SendAsPanel(Func<CommandId?> send)
    {
        if (_feed is not { } feed) return send();
        feed.Sending = true;
        try
        {
            return send();
        }
        finally
        {
            feed.Sending = false;
        }
    }

    private void UndoVoice()
    {
        if (_controller is not { } controller || !_inputModel.TryUndo(out var requestId, out var text)) return;
        controller.CancelPending(requestId);
        FocusInput(text);
    }

    private void ShowReview(TranscriptReview review)
    {
        _reviewing = true;
        FocusInput(review.Transcript.Text);
        // A LineEdit cannot tint words, so the first flagged word is selected for the player to check.
        if (review.Transcript.LowConfidenceWords is [var first, ..])
        {
            var at = review.Transcript.Text.IndexOf(first, StringComparison.Ordinal);
            if (at >= 0) _input.Select(at, at + first.Length);
        }
    }

    private void FocusInput(string text)
    {
        _input.Text = text;
        _input.GrabFocus();
        _input.CaretColumn = text.Length;
    }

    private void ShowPtt(PttState state)
    {
        _pttState = state;
        _ptt.Text = state switch
        {
            PttState.Listening => _labels?.PttListening ?? PttListeningText,
            PttState.Transcribing => _labels?.PttTranscribing ?? PttTranscribingText,
            PttState.Review => PttReviewText,
            PttState.Disabled => PttDisabledText,
            _ => _labels?.PttIdle(PushToTalkKeyName()) ?? PttIdleText,
        };
        _ptt.Disabled = state == PttState.Disabled || _mic is null;
        _pttLevel.Visible = state == PttState.Listening;
        RefreshProcess();
    }

    private void RefreshProcess() =>
        SetProcess((_pttState == PttState.Listening && _mic is not null) || _inputModel.UndoSecondsLeft.Value > 0);

    private void BuildQuickCommands(CommsController controller)
    {
        foreach (var button in _quickButtons)
        {
            _quickCommands.RemoveChild(button);
            button.QueueFree();
        }
        _quickButtons.Clear();
        foreach (var quick in controller.Comms.QuickCommands)
        {
            var id = quick.Id;
            var button = new Button { Text = quick.Label, ThemeTypeVariation = ThemeVariations.ButtonGhost };
            button.Pressed += () => _controller?.SendQuickCommand(id);
            button.SetMeta(RailsGate.ControlMeta, Usa.Simulation.Guidance.TutorialControls.Item(Usa.Simulation.Guidance.TutorialControls.CommsQuick, quick.Action.Value));
            _quickCommands.AddChild(button);
            _quickCommands.MoveChild(button, _quickButtons.Count);
            _quickButtons.Add(button);
        }
        ShowQuickCommands();
    }

    private void ShowQuickCommands()
    {
        var folded = _compact && !_more.ButtonPressed && _quickButtons.Count > CompactQuickCommands;
        for (var i = 0; i < _quickButtons.Count; i++) _quickButtons[i].Visible = !folded || i < CompactQuickCommands;
        _more.Visible = _compact && _quickButtons.Count > CompactQuickCommands;
    }

    private void BuildAddressees()
    {
        if (_crew is null) return;
        var selected = _addressee.Selected >= 0 && _addressee.Selected < _addresseeIds.Count ? _addresseeIds[_addressee.Selected] : null;
        _addressee.Clear();
        _addresseeIds.Clear();
        _addressee.AddItem(_labels?.AddresseeCrew ?? CrewWord);
        _addresseeIds.Add(null);
        foreach (var card in _crew.Cards)
        {
            _addressee.AddItem(card.Name.Value);
            _addresseeIds.Add(card.Id);
        }
        var index = Math.Max(0, _addresseeIds.IndexOf(selected));
        _addressee.Select(index);
        SelectAddressee(index);
    }

    private void TalkTo(CrewId crew)
    {
        var index = _addresseeIds.IndexOf(crew);
        if (index < 0) return;
        _addressee.Select(index);
        SelectAddressee(index);
        _input.GrabFocus();
    }

    private void SelectAddressee(int index)
    {
        if (_controller is null || index < 0 || index >= _addresseeIds.Count) return;
        _controller.Addressee = _addresseeIds[index];
    }

    /// <summary>The selector's wording for a crew member, so transcript rows name them as the player picked them.</summary>
    private string? CrewName(CrewId crew)
    {
        if (_crew is null) return null;
        foreach (var card in _crew.Cards)
        {
            if (card.Id == crew) return card.Name.Value;
        }
        return null;
    }

    private static string PushToTalkKeyName()
    {
        if (!InputMap.HasAction(InputBindings.PushToTalk)) return string.Empty;
        foreach (var bound in InputMap.ActionGetEvents(InputBindings.PushToTalk))
        {
            if (bound is InputEventKey key) return OS.GetKeycodeString(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode).ToUpperInvariant();
        }
        return string.Empty;
    }

    private void OnSearchInput(InputEvent input)
    {
        if (!input.IsActionPressed("ui_cancel")) return;
        _search.Clear();
        _feed?.SetSearch(null);
        _search.Visible = false;
        _input.GrabFocus();
        _search.AcceptEvent();
    }
}
