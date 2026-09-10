using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScriptedScreensFonts;

/// <summary>
/// Drives <see cref="FontRegistry"/> rescans. Bundled fonts do not exist yet when
/// LaunchPad loads mods, so a single scan at startup finds almost nothing.
/// </summary>
/// <remarks>
/// <c>Resources.FindObjectsOfTypeAll</c> walks every loaded object, so the burst after each
/// scene load stays short. It is followed by a slow heartbeat that never stops, because
/// fonts keep arriving long after the burst closes: signage faces load with the prefabs
/// that use them, which can be an hour into a session. A burst-only scan silently misses
/// those, and the miss looks like a broken mod rather than a timing gap.
/// </remarks>
internal sealed class FontRegistryLoader : MonoBehaviour
{
    private const int RescanCount = 15;
    private const float RescanIntervalSeconds = 2f;
    private const float HeartbeatIntervalSeconds = 20f;

    internal static void Install()
    {
        var host = new GameObject(nameof(ScriptedScreensFonts))
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        DontDestroyOnLoad(host);
        host.AddComponent<FontRegistryLoader>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        StartCoroutine(RescanWindow());
    }

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StopAllCoroutines();
        StartCoroutine(RescanWindow());
    }

    private static IEnumerator RescanWindow()
    {
        var burst = new WaitForSeconds(RescanIntervalSeconds);

        for (var i = 0; i < RescanCount; i++)
        {
            FontLoader.TryLoadPending();
            FontRegistry.ScanAndRegister();
            yield return burst;
        }

        // Slow tail: catches fonts that arrive with prefabs streamed in later. Only newly
        // seen names log anything, so a quiet session stays quiet.
        var heartbeat = new WaitForSeconds(HeartbeatIntervalSeconds);

        while (true)
        {
            FontLoader.TryLoadPending();
            FontRegistry.ScanAndRegister();
            yield return heartbeat;
        }
    }
}
