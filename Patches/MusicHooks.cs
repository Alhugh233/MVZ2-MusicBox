#nullable enable

using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MVZ2.Managers;
using MVZ2.Metas;
using MVZ2Logic.Saves;
using PVZEngine;
using UnityEngine;

namespace MusicBox.Patches;

[HarmonyPatch(typeof(LogicSaveExt), nameof(LogicSaveExt.IsMusicRoomUnlocked))]
internal static class MusicRoomUnlockPatch
{
    private static bool Prefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.GetAllMusicID))]
internal static class GetAllMusicIDPatch
{
    private static void Postfix(ref Il2CppReferenceArray<NamespaceID> __result)
    {
        if (!MusicRegistry.Ready)
            return;
        var extra = MusicRegistry.GetOrderedMainIDs();
        if (extra.Length == 0)
            return;

        var original = __result;
        int length = original?.Length ?? 0;
        var arr = new Il2CppReferenceArray<NamespaceID>(length + extra.Length);
        for (int i = 0; i < length; i++)
        {
            arr[i] = original![i];
        }
        for (int i = 0; i < extra.Length; i++)
        {
            arr[length + i] = extra[i];
        }
        __result = arr;
    }
}

[HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.GetMusicMeta))]
internal static class GetMusicMetaPatch
{
    private static bool Prefix(NamespaceID music, ref MusicMeta __result)
    {
        if (music == null)
            return true;
        if (Battle.BattleMusic.RedirectId != null
            && Battle.BattleMusic.RedirectKey == Battle.BattleMusic.Key(music)
            && MusicRegistry.Metas.TryGetValue(Battle.BattleMusic.RedirectId.Path, out var redirectMeta))
        {
            __result = redirectMeta;
            return false;
        }
        if (music.SpaceName == MusicRegistry.Namespace
            && MusicRegistry.Metas.TryGetValue(music.Path, out var meta))
        {
            __result = meta;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.GetMusicClip))]
internal static class GetMusicClipPatch
{
    private static bool Prefix(NamespaceID path, ref AudioClip __result)
    {
        if (path != null && path.SpaceName == MusicRegistry.Namespace)
        {
            if (!MusicRegistry.Clips.ContainsKey(path.Path))
                MusicPackLoader.EnsureClipAsync(path.Path);
            MusicRegistry.Clips.TryGetValue(path.Path, out var clip);
            __result = clip!;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(MVZ2.Audios.MusicManager), nameof(MVZ2.Audios.MusicManager.Update))]
internal static class MusicManagerUpdatePumpPatch
{
    private static void Postfix()
    {
        try
        {
            MusicPackLoader.Pump();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Pump failed: {e}");
        }
    }
}

[HarmonyPatch(typeof(MVZ2.MusicRoom.MusicRoomController), nameof(MVZ2.MusicRoom.MusicRoomController.Display))]
internal static class MusicRoomDisplayPatch
{
    private static void Postfix()
    {
        try
        {
            MusicPackLoader.EnsureClipsDecoded();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Pre-decode failed: {e}");
        }
    }
}
