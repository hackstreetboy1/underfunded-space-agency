using Godot;
using Usa.Game.Presentation.Audio;
using Usa.Game.Presentation.Audio.Mixing;
using Usa.Game.Presentation.Cues;

namespace Usa.Game.UI.Sound;

/// <summary>
/// UI ticks for a whole scene (GDD 14): one call from the scene root's <c>_Ready</c>,
/// <c>UiSoundHooks.Attach(this, services.Audio);</c>, and every button, toggle, check box, option/menu item, tab and slider
/// under the root makes <see cref="AudioCue.UiTick"/> on the UI bus when the player uses it, including controls added
/// later (lists, dialogs). Only player input sounds: a control changed from code, or a disabled one, stays silent.
/// </summary>
/// <remarks>
/// Tag a control, or any ancestor, with the <see cref="UiSoundCue.MetaKey"/> metadata (<c>none</c>, <c>tick</c>,
/// <c>confirm</c>, <c>error</c>) to silence it or give it another UI cue; the nearest tag wins. Each control is hooked once,
/// so nested scenes that also attach do not double the tick. UI-bus sounds keep playing while the game is frozen (the
/// director holds every other bus), so the Esc menu still ticks; the UI volume slider applies.
/// </remarks>
public sealed partial class UiSoundHooks : Node
{
    private static readonly StringName MetaKey = UiSoundCue.MetaKey;
    private static readonly StringName HookedMeta = "_usa_ui_sound_hooked";

    private Action<AudioCue> _play = static _ => { };
    private Node? _root;

    /// <summary>Hooks the controls under <paramref name="root"/> to the app's audio director.</summary>
    public static UiSoundHooks Attach(Node root, AudioDirector audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return Attach(root, cue => audio.Play(cue));
    }

    /// <summary>Hooks the controls under <paramref name="root"/>; <paramref name="play"/> receives each interaction's cue.</summary>
    public static UiSoundHooks Attach(Node root, Action<AudioCue> play)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(play);
        var hooks = new UiSoundHooks { Name = nameof(UiSoundHooks), _play = play };
        root.AddChild(hooks);
        return hooks;
    }

    public override void _EnterTree()
    {
        _root = GetParent();
        if (_root is not null) HookSubtree(_root);
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= OnNodeAdded;
        _root = null;
    }

    private void OnNodeAdded(Node node)
    {
        if (_root is not null && _root.IsAncestorOf(node)) Hook(node);
    }

    private void HookSubtree(Node node)
    {
        Hook(node);
        foreach (var child in node.GetChildren(includeInternal: true)) HookSubtree(child);
    }

    // PopupMenu covers option-button, menu-button and context-menu items; an internal TabBar covers its TabContainer.
    private void Hook(Node node)
    {
        if (node is not (BaseButton or Slider or TabBar or PopupMenu) || node.HasMeta(HookedMeta)) return;
        node.SetMeta(HookedMeta, true);
        switch (node)
        {
            case BaseButton button:
                button.Pressed += () => Activate(button);
                break;
            case Slider slider:
                slider.DragEnded += changed => { if (changed) Activate(slider); };
                break;
            case TabBar tabs:
                tabs.TabClicked += _ => Activate(tabs);
                break;
            case PopupMenu popup:
                popup.IdPressed += _ => Activate(popup);
                break;
        }
    }

    private void Activate(Node control)
    {
        if (!IsInstanceValid(this) || !IsInstanceValid(control)) return;
        if (UiSoundCue.Resolve(TagsOutwardFrom(control)) is { } cue) _play(cue);
    }

    private static IEnumerable<string?> TagsOutwardFrom(Node control)
    {
        for (var node = control; node is not null; node = node.GetParent())
        {
            yield return node.HasMeta(MetaKey) ? node.GetMeta(MetaKey).AsString() : null;
        }
    }
}
