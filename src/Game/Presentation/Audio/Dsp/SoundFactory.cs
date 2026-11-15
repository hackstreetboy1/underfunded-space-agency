using Usa.Game.Presentation.Audio.Dsp.Sounds;

namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>
/// Creates generators. Loop tables are synthesized once per sound (fixed seed) and shared by every instance. After
/// <see cref="Prewarm"/>, one-shots are not synthesized either: each plays one of <see cref="OneShotVariants"/>
/// pre-rendered seed variants (seed modulo the variant count), so no play costs a synthesis pass on the main thread.
/// Without <see cref="Prewarm"/> every one-shot is synthesized from its own seed (offline previews).
/// </summary>
public sealed class SoundFactory
{
    /// <summary>Seed variants kept per one-shot: enough that repeated thruster pulses and ticks do not sound identical.</summary>
    public const int OneShotVariants = 4;

    private const uint LoopTableSeed = 0x5EED_0001;
    private readonly Dictionary<SoundId, float[]> _tables = [];
    private readonly Dictionary<SoundId, float[][]> _oneShots = [];

    public bool IsPrewarmed { get; private set; }

    public ISoundGenerator Create(SoundId id, uint seed, float intensity = 1f)
    {
        var generator = Synthesize(id, seed, intensity);
        if (generator is OneShotSound oneShot && _oneShots.TryGetValue(id, out var variants))
            oneShot.UseRendered(variants[seed % (uint)variants.Length]);
        return generator;
    }

    /// <summary>
    /// Builds every loop table and every one-shot variant up front (a few hundred milliseconds of CPU in total), so it
    /// belongs in a load step, never in a frame that plays sound.
    /// </summary>
    public void Prewarm()
    {
        foreach (var info in SoundCatalog.All)
        {
            if (info.IsLoop)
            {
                Table(info.Id);
                continue;
            }

            if (_oneShots.ContainsKey(info.Id)) continue;
            var variants = new float[OneShotVariants][];
            for (var i = 0; i < variants.Length; i++)
            {
                variants[i] = ((OneShotSound)Synthesize(info.Id, (uint)i, 1f)).Rendered;
            }

            _oneShots[info.Id] = variants;
        }

        IsPrewarmed = true;
    }

    private ISoundGenerator Synthesize(SoundId id, uint seed, float intensity) => id switch
    {
        SoundId.PttOpen => new PttOpen(seed) { Intensity = intensity },
        SoundId.PttClose => new PttClose(seed) { Intensity = intensity },
        SoundId.AlarmCaution => new AlarmCaution(seed) { Intensity = intensity },
        SoundId.AlarmWarning => new AlarmWarning(seed) { Intensity = intensity },
        SoundId.AlarmReminder => new AlarmReminder(seed) { Intensity = intensity },
        SoundId.ThrusterPulse => new ThrusterPulse(seed) { Intensity = intensity },
        SoundId.ChuteSnap => new ChuteSnap(seed) { Intensity = intensity },
        SoundId.Splash => new Splash(seed) { Intensity = intensity },
        SoundId.UiTick => new UiTick(seed) { Intensity = intensity },
        SoundId.UiConfirm => new UiConfirm(seed) { Intensity = intensity },
        SoundId.UiError => new UiError(seed) { Intensity = intensity },
        SoundId.TipChime => new TipChime(seed) { Intensity = intensity },
        SoundId.RadioStaticBed => new RadioStaticBed(Table(id), intensity),
        SoundId.CabinHumVentBed => new CabinHumVentBed(Table(id), intensity),
        SoundId.AlarmMaster => new AlarmMaster(Table(id), intensity),
        SoundId.StuckThrusterHiss => new StuckThrusterHiss(Table(id), intensity),
        SoundId.RetroRumble => new RetroRumble(Table(id), intensity),
        SoundId.EntryRoar => new EntryRoar(Table(id), intensity),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown sound."),
    };

    private float[] Table(SoundId id)
    {
        if (_tables.TryGetValue(id, out var table)) return table;
        table = id switch
        {
            SoundId.RadioStaticBed => RadioStaticBed.BuildTable(LoopTableSeed),
            SoundId.CabinHumVentBed => CabinHumVentBed.BuildTable(LoopTableSeed),
            SoundId.AlarmMaster => AlarmMaster.BuildTable(),
            SoundId.StuckThrusterHiss => StuckThrusterHiss.BuildTable(LoopTableSeed),
            SoundId.RetroRumble => RetroRumble.BuildTable(LoopTableSeed),
            SoundId.EntryRoar => EntryRoar.BuildTable(LoopTableSeed),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Not a loop."),
        };
        _tables[id] = table;
        return table;
    }
}
