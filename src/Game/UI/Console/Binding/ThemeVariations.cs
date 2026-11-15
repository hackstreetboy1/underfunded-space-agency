using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;

namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// Theme type variations of <c>UsaTheme.tres</c> (and its high-contrast twin) the console scenes switch between at
/// runtime. Colours stay in the theme: views pick a variation, never a colour, wherever the theme has one.
/// </summary>
public static class ThemeVariations
{
    /// <summary>The control's own theme type (no variation).</summary>
    public const string Default = "";

    public const string Badge = "Badge";
    public const string BadgeLabel = "BadgeLabel";
    public const string ButtonPrimary = "ButtonPrimary";
    public const string ButtonGhost = "ButtonGhost";
    public const string ButtonDanger = "ButtonDanger";
    public const string PanelHeader = "PanelHeader";
    public const string PanelHeaderTitle = "PanelHeaderTitle";
    public const string Program2Panel = "Program2Panel";
    public const string Program2PanelHeader = "Program2PanelHeader";
    public const string Program2PanelHeaderTitle = "Program2PanelHeaderTitle";
    public const string AlarmRowMasterLabel = "AlarmRowMasterLabel";
    public const string Small = "Small";
    public const string Meta = "Meta";
    public const string ReadoutSmall = "ReadoutSmall";

    /// <summary>Every variation this class can return, for the theme coverage test.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Badge, BadgeLabel, ButtonPrimary, ButtonGhost, ButtonDanger, PanelHeader, PanelHeaderTitle, Small, Meta, ReadoutSmall,
        Program2Panel, Program2PanelHeader, Program2PanelHeaderTitle, AlarmRowMasterLabel,
        "BadgeOk", "BadgeCaution", "BadgeCritical", "BadgeConflict", "BadgeUnknown", "BadgeInfo", "BadgeAccent",
        "BadgeLabelOk", "BadgeLabelCaution", "BadgeLabelCritical", "BadgeLabelConflict", "BadgeLabelUnknown", "BadgeLabelInfo",
        "BadgeLabelAccent", "AlarmRow", "AlarmRowCaution", "AlarmRowWarning", "AlarmRowMaster",
    ];

    /// <summary>Badge background for a colour token; neutral for text tones and unknown keys.</summary>
    public static string BadgePanel(string? colourToken) => Badge + ToneSuffix(colourToken);

    /// <summary>Badge word style matching <see cref="BadgePanel"/>.</summary>
    public static string BadgeText(string? colourToken) => BadgeLabel + ToneSuffix(colourToken);

    public static string AlarmRowPanel(AlarmPriority priority) => priority switch
    {
        AlarmPriority.Caution => "AlarmRowCaution",
        AlarmPriority.Warning => "AlarmRowWarning",
        AlarmPriority.Master => "AlarmRowMaster",
        _ => "AlarmRow",
    };

    /// <summary>Text on an alarm row: dark on the solid master fill, the default label style elsewhere.</summary>
    public static string AlarmRowText(AlarmPriority priority) =>
        priority == AlarmPriority.Master ? AlarmRowMasterLabel : Default;

    /// <summary>A stage or complexity pip: filled pips take the info badge style, empty ones the neutral badge.</summary>
    public static string Pip(bool filled) => filled ? BadgePanel(ColourTokens.Info) : Badge;

    public static string PanelHeaderFor(bool legacy) => legacy ? Program2PanelHeader : PanelHeader;

    public static string PanelHeaderTitleFor(bool legacy) => legacy ? Program2PanelHeaderTitle : PanelHeaderTitle;

    private static string ToneSuffix(string? colourToken) => colourToken switch
    {
        ColourTokens.Ok => "Ok",
        ColourTokens.Caution => "Caution",
        ColourTokens.Critical => "Critical",
        ColourTokens.Conflict => "Conflict",
        ColourTokens.Unknown => "Unknown",
        ColourTokens.Info => "Info",
        ColourTokens.Accent => "Accent",
        _ => string.Empty,
    };
}
