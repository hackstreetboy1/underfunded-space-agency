using Usa.Game.Services.Saves;

namespace Usa.Game.Flow.Profile;

/// <summary>The parts of <see cref="SaveService"/> the front end uses (see <see cref="SaveServiceProfileStore"/>).</summary>
public interface IProfileStore
{
    /// <summary>Never null: an empty profile when the file is missing or unreadable.</summary>
    PlayerProfile LoadProfile();

    /// <summary>Valid resume files only.</summary>
    IReadOnlyList<ResumeMetadata> ListResumes();

    ResumeSave? LoadResume(string missionId);

    /// <summary>Merges a finished run into the profile; false when nothing was written.</summary>
    bool RecordRun(CompletedRun run);

    void DeleteResume(string missionId);
}
