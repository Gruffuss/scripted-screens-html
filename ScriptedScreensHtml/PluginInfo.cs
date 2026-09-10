namespace ScriptedScreensHtml;

internal static partial class PluginInfo
{
    internal const string PLUGIN_GUID = "gruffuss.stationeers.scriptedscreens.html";
    internal const string PLUGIN_NAME = "ScriptedScreens Html";

    internal static readonly string PLUGIN_VERSION =
        typeof(PluginInfo).Assembly.GetName().Version?.ToString() ?? PLUGIN_VERSION_CONST;
}
