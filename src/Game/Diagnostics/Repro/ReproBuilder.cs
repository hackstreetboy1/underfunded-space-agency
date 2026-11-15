using System.Text.Json;
using Usa.Simulation.Session;

namespace Usa.Game.Diagnostics.Repro;

/// <summary>What the host knows about a capture besides the session itself.</summary>
public sealed record ReproContext(
    DateTimeOffset CreatedUtc,
    string Reason,
    ReproApp App,
    ReproSettings? Settings,
    ReproHost? Host,
    ReproFailure? Failure = null,
    string? Note = null);

/// <summary>Builds a <see cref="ReproDocument"/> from a live session. Computes the state hash, so call it on demand, not per frame.</summary>
public static class ReproBuilder
{
    /// <param name="session">The running session, or null to record only the app context (e.g. a failure on the main menu).</param>
    public static ReproDocument Build(ReproContext context, MissionSession? session, bool guidedFirstRun = false, bool resumed = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (session is null)
            return new ReproDocument(ReproDocument.SchemaTag, context.CreatedUtc, context.Reason, context.App, null, context.Settings, context.Host, [], null, context.Failure, context.Note);

        var samples = session.HashSamples;
        var hashes = new ReproHash[samples.Count];
        for (var i = 0; i < samples.Count; i++) hashes[i] = new ReproHash(samples[i].Time.Milliseconds, samples[i].Hash);

        using var log = JsonDocument.Parse(session.CommandLog.ToJson());
        var mission = new ReproMission(
            session.MissionId,
            session.DifficultyId,
            session.Seed,
            RunTag(session),
            session.SessionId,
            session.Now.Milliseconds,
            session.StateHash(),
            session.CommandLog.Entries.Count,
            guidedFirstRun,
            resumed);
        return new ReproDocument(
            ReproDocument.SchemaTag, context.CreatedUtc, context.Reason, context.App, mission, context.Settings, context.Host, hashes,
            log.RootElement.Clone(), context.Failure, context.Note);
    }

    /// <summary>The launch's run tag: <see cref="MissionSession.CommandIdPrefix"/> minus "&lt;session id&gt;-"; null when untagged.</summary>
    public static string? RunTag(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var prefix = session.CommandIdPrefix;
        var id = session.SessionId;
        return prefix.Length > id.Length + 1 && prefix.StartsWith(id, StringComparison.Ordinal) ? prefix[(id.Length + 1)..] : null;
    }
}
