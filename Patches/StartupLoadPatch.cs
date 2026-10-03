#nullable enable

using System;
using HarmonyLib;
using MVZ2.Managers;

namespace MusicBox.Patches;

[HarmonyPatch(typeof(MainManager), nameof(MainManager.Initialize))]
internal static class StartupLoadPatch
{
    private static void Postfix()
    {
        try
        {
            Plugin.Log.LogInfo("[MusicBox] Loading music packs at startup.");
            MusicPackLoader.Reload();
            Battle.BattleMusic.Load();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Startup load failed: {e}");
        }
    }
}
