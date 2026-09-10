namespace ScriptedScreensFonts;

/// <summary>
/// Mod identity for LaunchPad.
/// </summary>
/// <remarks>
/// <see cref="PLUGIN_VERSION_CONST"/> is generated at compile time from the csproj
/// <c>Version</c> property (see the <c>GenerateVersionConst</c> target).
/// </remarks>
internal static partial class PluginInfo
{
    /// <summary>Stable mod identity (Harmony ID, Workshop metadata, etc.).</summary>
    internal const string PLUGIN_GUID = "gruffuss.stationeers.scriptedscreens.fonts";

    /// <summary>Human-readable mod name for logs and the mod list.</summary>
    internal const string PLUGIN_NAME = "ScriptedScreens Fonts";

    internal static readonly string PLUGIN_VERSION =
        typeof(PluginInfo).Assembly.GetName().Version?.ToString() ?? PLUGIN_VERSION_CONST;
}
