#nullable enable

using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using LanguagePackEnhance;

namespace MusicBox;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.mvz2.languagepackenhance")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    public override void Load()
    {
        Log = base.Log;

        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();

        try
        {
            LanguagePackEnhanceApi.RegisterMod("MusicBox", "MusicBox.lang.json");
        }
        catch (System.Exception e)
        {
            Log.LogError($"[MusicBox] Register UI language pack failed: {e}");
        }

        ClassInjector.RegisterTypeInIl2Cpp<MusicBoxBehaviour>();
        var go = new UnityEngine.GameObject("MusicBox");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<MusicBoxBehaviour>();

        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
}
