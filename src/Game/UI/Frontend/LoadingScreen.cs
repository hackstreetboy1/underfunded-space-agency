using Godot;
using Usa.Game.Flow;
using Usa.Game.Flow.Screens;
using Usa.Game.UI.Frontend.Model;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Loading (<c>Loading.tscn</c>, Figma G6-10; UI.md 5): agency mark, "Loading mission" with title and era · setting ·
/// difficulty, one humour line and an encyclopedia tip picked from the launch seed (<see cref="LoadingModel"/>), and a 720 px
/// bar showing the launch's real progress (<c>MissionFlowHost.LoadProgress</c>: data load, session compose, scene instancing) with
/// the stage under it. No input. Behind it all, darkened by the shade so the bar and text stay readable: the Astronomy
/// Picture of the Day (<see cref="ApodBackdrop"/>, aspect-filled, with its date, title when allowed and credit at top right),
/// else the curated main-menu image with its label. A picture that arrives while the screen is open fades in.
/// </summary>
public sealed partial class LoadingScreen : Control, IFrontendScreen
{
    private FrontendContext? _context;
    private LoadProgress? _progress;
    private Label _mark = null!;
    private Label _missionLine = null!;
    private Label _eyebrow = null!;
    private Label _title = null!;
    private Label _subtitle = null!;
    private Label _line = null!;
    private Control _barHost = null!;
    private Label _status = null!;
    private PanelContainer _tip = null!;
    private Label _tipPrefix = null!;
    private Label _tipTitle = null!;
    private Label _tipText = null!;
    private Label _tipSource = null!;
    private ProgressBar? _bar;
    private TextureRect _backdrop = null!;
    private TextureRect _apodImage = null!;
    private ColorRect _shade = null!;
    private VBoxContainer _caption = null!;
    private Label _captionHeading = null!;
    private Label _captionTitle = null!;
    private Label _captionCredit = null!;
    private Tween? _fade;

    private const double FadeSeconds = 0.6;
    private const float ShadeAlpha = 0.72f;

    public override void _Ready()
    {
        _mark = GetNode<Label>("%Mark");
        _missionLine = GetNode<Label>("%MissionLine");
        _eyebrow = GetNode<Label>("%Eyebrow");
        _title = GetNode<Label>("%Title");
        _subtitle = GetNode<Label>("%Subtitle");
        _line = GetNode<Label>("%Line");
        _barHost = GetNode<Control>("%BarHost");
        _status = GetNode<Label>("%Status");
        _tip = GetNode<PanelContainer>("%Tip");
        _tipPrefix = GetNode<Label>("%TipPrefix");
        _tipTitle = GetNode<Label>("%TipTitle");
        _tipText = GetNode<Label>("%TipText");
        _tipSource = GetNode<Label>("%TipSource");
        _backdrop = GetNode<TextureRect>("%Backdrop");
        _apodImage = GetNode<TextureRect>("%ApodImage");
        _shade = GetNode<ColorRect>("%Shade");
        _caption = GetNode<VBoxContainer>("%Caption");
        _captionHeading = GetNode<Label>("%CaptionHeading");
        _captionTitle = GetNode<Label>("%CaptionTitle");
        _captionCredit = GetNode<Label>("%CaptionCredit");
        SetProcess(false);
    }

    // Presentation only: waits for the picture the boot fetch may still be bringing, then fades it in.
    public override void _Process(double delta)
    {
        if (_context?.Apod is not { } apod)
        {
            SetProcess(false);
            return;
        }

        if (apod.TryGet(out var texture, out var caption))
        {
            SetProcess(false);
            ShowApod(texture, caption, fade: !_context.ReducedMotion);
        }
        else if (!apod.Pending)
        {
            SetProcess(false);
        }
    }

    public override void _ExitTree()
    {
        _fade?.Kill();
        _fade = null;
        if (_progress is not null) _progress.Changed -= ShowProgress;
        _progress = null;
    }

    private void ShowProgress()
    {
        if (_progress is not { } progress || _context is not { } context || _bar is null) return;
        _bar.Value = LoadingText.BarValue(progress.Stage, progress.Fraction);
        _status.Text = context.Text.Get(LoadingText.StatusKey(progress.Stage));
    }

    public void Initialize(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Text;
        var tokens = context.Tokens;
        _mark.Text = text.Get("agency.headerMark");
        _eyebrow.Text = text.Get("briefing.loadingTitle").ToUpperInvariant();
        _status.Text = text.Get("briefing.loadingStatus");

        var launch = context.Router.CurrentLaunch;
        var content = launch is null ? null : context.Content(launch.MissionId);
        if (launch is not null && content is not null)
        {
            var briefing = content.Briefing;
            _title.Text = briefing.Title;
            _missionLine.Text = briefing.Vehicles.Count > 0 ? $"{briefing.Title} · {briefing.Vehicles[0].Name}" : briefing.Title;
            var setting = context.Catalog()?.Find(launch.MissionId)?.Setting;
            _subtitle.Text = MenuText.LoadingSubtitle(briefing.Header.Era, setting, context.DifficultyLabel(launch.Difficulty));
        }

        if (context.Encyclopedia is { } encyclopedia && launch is not null)
        {
            var loading = new LoadingModel(context.Strings, encyclopedia, launch.Seed ?? (ulong)launch.RunNumber);
            _line.Text = loading.Line;
            _tip.Visible = loading.TipTitle is not null;
            _tipPrefix.Text = loading.TipPrefix;
            _tipTitle.Text = loading.TipTitle ?? string.Empty;
            _tipText.Text = loading.TipText ?? string.Empty;
            var source = loading.TipEntryId is { } id && encyclopedia.TryGet(id, out var entry) && entry.Sources.Count > 0 ? entry.Sources[0] : null;
            _tipSource.Visible = source is not null;
            if (source is not null)
            {
                var name = source.Author is null ? $"{source.Title} ({source.Publisher})" : $"{source.Title}, {source.Author} ({source.Publisher})";
                _tipSource.Text = text.Format("briefing.loadingSource", new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = name });
            }
        }
        else
        {
            _tip.Visible = false;
        }

        ShowBackdrop(context);

        Ui.Clear(_barHost);
        _bar = Ui.Bar(0, tokens.ColorTextMid, tokens, 4);
        _barHost.AddChild(_bar);

        // The bar jumps from stage to stage; nothing animates, so reduced motion needs no special case.
        _context = context;
        if (_progress is not null) _progress.Changed -= ShowProgress;
        _progress = context.Services.Flow.LoadProgress;
        _progress.Changed += ShowProgress;
        ShowProgress();
    }

    private void ShowBackdrop(FrontendContext context)
    {
        _shade.Color = context.Tokens.ColorBg0 with { A = ShadeAlpha };
        if (context.Apod is { } apod && apod.TryGet(out var texture, out var caption))
        {
            ShowApod(texture, caption, fade: false);
            return;
        }

        var curated = MenuBackdrop.Pick(context.Manifest, context.Services.LaunchCount, context.Strings);
        var curatedTexture = Ui.LoadImage(curated?.LocalPath);
        _backdrop.Texture = curatedTexture;
        _backdrop.Visible = curatedTexture is not null;
        _shade.Visible = curatedTexture is not null;
        _caption.Visible = curatedTexture is not null && curated is not null;
        if (curated is not null)
        {
            _captionHeading.Text = curated.Label;
            _captionTitle.Visible = false;
            _captionCredit.Visible = false;
        }

        SetProcess(context.Apod?.Pending ?? false);
    }

    private void ShowApod(Texture2D texture, ApodCaption caption, bool fade)
    {
        _apodImage.Texture = texture;
        _apodImage.Visible = true;
        _shade.Visible = true;
        _captionHeading.Text = caption.Heading;
        _captionTitle.Text = caption.Title ?? string.Empty;
        _captionTitle.Visible = caption.Title is not null;
        _captionCredit.Text = caption.Credit;
        _captionCredit.Visible = true;
        _caption.Visible = true;

        _fade?.Kill();
        if (!fade)
        {
            _apodImage.Modulate = Colors.White;
            _caption.Modulate = Colors.White;
            return;
        }

        _apodImage.Modulate = Colors.Transparent;
        _caption.Modulate = Colors.Transparent;
        _fade = CreateTween().SetParallel();
        _fade.TweenProperty(_apodImage, "modulate", Colors.White, FadeSeconds);
        _fade.TweenProperty(_caption, "modulate", Colors.White, FadeSeconds);
    }
}
