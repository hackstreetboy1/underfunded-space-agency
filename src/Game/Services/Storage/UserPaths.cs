using Godot;

namespace Usa.Game.Services.Storage;

/// <summary>Absolute OS paths for <c>user://</c> locations, so pure services can use System.IO.</summary>
public static class UserPaths
{
    public static string Root => ProjectSettings.GlobalizePath("user://");

    public static string Logs => ProjectSettings.GlobalizePath("user://logs");
}
