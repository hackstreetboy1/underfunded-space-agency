using Usa.Game.Services.Saves;

namespace Usa.Game.Flow.Profile;

public sealed class SaveServiceProfileStore(SaveService saves) : IProfileStore
{
    public PlayerProfile LoadProfile() => saves.LoadProfile().Value ?? new PlayerProfile();

    public IReadOnlyList<ResumeMetadata> ListResumes() => saves.ListResumes();

    public ResumeSave? LoadResume(string missionId) => saves.LoadResume(missionId) is { Status: LoadStatus.Ok, Value: { } save } ? save : null;

    public bool RecordRun(CompletedRun run) => saves.RecordRun(run) == SaveStatus.Ok;

    public void DeleteResume(string missionId) => saves.DeleteResume(missionId);
}
