using System.Globalization;
using Usa.Game.Services.Storage;

namespace Usa.Game.Flow.Profile;

/// <summary>Counts game launches in <c>user://launch_count.txt</c>; the main menu rotates its backdrop by it (UI.md 5).</summary>
public sealed class LaunchCounter(UserFileStore store)
{
    public const string FileName = "launch_count.txt";

    /// <summary>Adds this launch and returns the total (1 on the first launch). A damaged or unwritable file never stops the game.</summary>
    public int RegisterLaunch()
    {
        var read = store.ReadText(FileName);
        var previous = read.Status == StoreReadStatus.Ok
                       && int.TryParse(read.Text!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        var count = previous == int.MaxValue ? 1 : previous + 1;
        store.WriteTextAtomic(FileName, count.ToString(CultureInfo.InvariantCulture));
        return count;
    }
}
