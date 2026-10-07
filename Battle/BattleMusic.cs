#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using MVZ2.Audios;
using MVZ2.Managers;
using MVZ2Logic.Level;
using PVZEngine;
using PVZEngine.Level;

namespace MusicBox.Battle;

internal static class BattleMusic
{
    public static string FilePath => Path.Combine(MusicRegistry.BaseDir, "battle_music.json");

    public static readonly Dictionary<string, string> AreaMusic = new Dictionary<string, string>();
    public static readonly Dictionary<string, string> MapMusic = new Dictionary<string, string>();
    public static readonly Dictionary<string, List<string>> StageSlots = new Dictionary<string, List<string>>();
    public static string? RedirectKey;
    public static NamespaceID? RedirectId;

    private static readonly Dictionary<string, string[]> BossPhases = new Dictionary<string, string[]>
    {
        { "mvz2:halloween_11", new[] { "mvz2:ultimate_battle", "mvz2:halloween_boss" } },
        { "mvz2:halloween_15", new[] { "mvz2:ultimate_battle", "mvz2:halloween_boss" } },
        { "mvz2:dream_11", new[] { "mvz2:nightmare_final", "mvz2:nightmare_boss", "mvz2:nightmare_boss2" } },
        { "mvz2:castle_11", new[] { "mvz2:castle_final", "mvz2:wither_boss" } },
        { "mvz2:castle_15", new[] { "mvz2:castle_final", "mvz2:seija" } },
        { "mvz2:mausoleum_11", new[] { "mvz2:mausoleum_final", "mvz2:mausoleum_boss", "mvz2:mausoleum_boss_2" } },
        { "mvz2:ship_11", new[] { "mvz2:ship_final", "mvz2:ship_boss" } },
        { "mvz2:palace_11", new[] { "mvz2:palace_final", "mvz2:palace_boss" } },
    };

    public static string Key(NamespaceID id)
    {
        return $"{id.SpaceName}:{id.Path}";
    }

    public static NamespaceID? Parse(string s)
    {
        if (string.IsNullOrEmpty(s))
            return null;
        return NamespaceID.Parse(s, MainManager.Instance.BuiltinNamespace);
    }

    public static string[] GetBossPhaseKeys(NamespaceID stageID)
    {
        return BossPhases.TryGetValue(Key(stageID), out var arr) ? arr : Array.Empty<string>();
    }

    public sealed class MapPresetInfo
    {
        public NamespaceID Music = null!;
        public string Id = string.Empty;
    }

    public sealed class ChapterInfo
    {
        public NamespaceID Area = null!;
        public string Name = string.Empty;
        public readonly List<NamespaceID> BattleMusics = new List<NamespaceID>();
        public readonly List<MapPresetInfo> MapPresets = new List<MapPresetInfo>();
        public readonly List<NamespaceID> BossStages = new List<NamespaceID>();
    }

    private static readonly Dictionary<string, string[]> ExtraBattleMusics = new Dictionary<string, string[]>
    {
        { "mvz2:dream", new[] { "mvz2:nightmare_level" } },
    };

    public static List<ChapterInfo> EnumerateChapters()
    {
        var result = new List<ChapterInfo>();
        var main = MainManager.Instance;
        var nsp = main.BuiltinNamespace;
        var maps = main.ResourceManager.GetModMapMetas(nsp);
        var stageMetas = main.ResourceManager.GetModStageMetas(nsp);
        var typeDict = new Dictionary<string, string>();
        foreach (var sm in stageMetas)
        {
            if (sm != null)
                typeDict[sm.ID] = sm.Type;
        }
        if (maps == null)
            return result;

        foreach (var map in maps)
        {
            if (map == null || !NamespaceID.IsValid(map.area))
                continue;
            var chapter = new ChapterInfo
            {
                Area = map.area!,
                Name = map.area!.Path,
            };
            var groupID = new NamespaceID(main.BuiltinNamespace, "chapter." + map.area!.Path);
            if (!main.SaveManager.IsGroupUnlocked(groupID))
                continue;
            var areaMeta = main.ResourceManager.GetAreaMeta(map.area!);
            if (areaMeta?.MusicID != null)
                chapter.BattleMusics.Add(areaMeta.MusicID);
            if (ExtraBattleMusics.TryGetValue(Key(map.area!), out var extras))
            {
                foreach (var extra in extras)
                {
                    var id = Parse(extra);
                    if (id != null && !chapter.BattleMusics.Contains(id))
                        chapter.BattleMusics.Add(id);
                }
            }
            if (map.presets != null)
            {
                foreach (var preset in map.presets)
                {
                    if (preset == null || !NamespaceID.IsValid(preset.music))
                        continue;
                    var presetId = NamespaceID.IsValid(preset.id) ? preset.id.Path : preset.music!.Path;
                    chapter.MapPresets.Add(new MapPresetInfo { Music = preset.music!, Id = presetId });
                }
            }
            if (map.stages != null)
            {
                foreach (var st in map.stages)
                {
                    if (st == null || !NamespaceID.IsValid(st.stage))
                        continue;
                    if (typeDict.TryGetValue(st.stage.Path, out var type) && type == "boss")
                        chapter.BossStages.Add(st.stage);
                }
            }
            result.Add(chapter);
        }
        return result;
    }

    public static void Load()
    {
        AreaMusic.Clear();
        StageSlots.Clear();
        try
        {
            if (!File.Exists(FilePath))
                return;
            var root = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(FilePath)));
            if (root == null)
                return;
            var areas = MiniJson.Obj(root.GetValueOrDefault("areas"));
            if (areas != null)
            {
                foreach (var pair in areas)
                    AreaMusic[pair.Key] = MiniJson.Str(pair.Value);
            }
            var maps = MiniJson.Obj(root.GetValueOrDefault("maps"));
            if (maps != null)
            {
                foreach (var pair in maps)
                    MapMusic[pair.Key] = MiniJson.Str(pair.Value);
            }
            var stages = MiniJson.Obj(root.GetValueOrDefault("stages"));
            if (stages != null)
            {
                foreach (var pair in stages)
                {
                    var arr = MiniJson.Arr(pair.Value);
                    if (arr == null)
                        continue;
                    var list = new List<string>();
                    foreach (var item in arr)
                        list.Add(MiniJson.Str(item));
                    StageSlots[pair.Key] = list;
                }
            }
            foreach (var key in AreaMusic.Keys)
                Diag.Log($"[MusicBox] battle_music area key: {key}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Failed to load battle_music.json: {e}");
        }
    }

    public static void Save()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("{\n    \"areas\": {");
            bool first = true;
            foreach (var pair in AreaMusic)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append("\n        ").Append('"').Append(Escape(pair.Key)).Append("\": \"").Append(Escape(pair.Value)).Append('"');
            }
            sb.Append("\n    },\n    \"maps\": {");
            first = true;
            foreach (var pair in MapMusic)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append("\n        ").Append('"').Append(Escape(pair.Key)).Append("\": \"").Append(Escape(pair.Value)).Append('"');
            }
            sb.Append("\n    },\n    \"stages\": {");
            first = true;
            foreach (var pair in StageSlots)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append("\n        ").Append('"').Append(Escape(pair.Key)).Append("\": [");
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    sb.Append('"').Append(Escape(pair.Value[i])).Append('"');
                }
                sb.Append(']');
            }
            sb.Append("\n    }\n}\n");
            File.WriteAllText(FilePath, sb.ToString());
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Failed to save battle_music.json: {e}");
        }
    }

    private static string Escape(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public static bool TryResolve(NamespaceID requested, out NamespaceID? result)
    {
        result = null;
        try
        {
            var main = MainManager.Instance;
            var mapArea = FindMapArea(requested);
            if (mapArea != null)
            {
                Diag.Log($"[MusicBox] Map music {Key(requested)} area={mapArea} hasMapKey={MapMusic.ContainsKey(Key(requested))}");
                if (MapMusic.TryGetValue(Key(requested), out var mv) && !string.IsNullOrEmpty(mv))
                {
                    var mid = Parse(mv);
                    if (NamespaceID.IsValid(mid))
                    {
                        result = MusicRegistry.ClipFiles.ContainsKey(mid!.Path + "_sub")
                            ? new NamespaceID(MusicRegistry.Namespace, mid.Path + "#seq")
                            : mid;
                        if (Prepare(result))
                            return true;
                        result = null;
                        return false;
                    }
                }
                return false;
            }
            var level = main.LevelManager.GetLevel();
            if (level == null)
                return false;
            var stageDef = level.StageDefinition;
            if (stageDef == null)
                return false;
            var type = LogicStageProps.GetStageType(stageDef);
            if (type != "normal" && type != "boss")
                return false;
            var stageKey = Key(stageDef.GetID());
            Diag.Log($"[MusicBox] Level music {Key(requested)} stage={stageKey} type={type} area={Key(level.AreaDefinition.GetID())} hasAreaKey={AreaMusic.ContainsKey(Key(requested))}");

            if (type == "boss")
            {
                if (!BossPhases.TryGetValue(stageKey, out var phases))
                    return false;
                int slot = IndexOf(phases, Key(requested));
                if (slot < 0)
                    return false;
                if (!StageSlots.TryGetValue(stageKey, out var list) || slot >= list.Count)
                    return false;
                var id = Parse(list[slot]);
                if (!NamespaceID.IsValid(id))
                    return false;
                result = id;
                if (Prepare(result))
                    return true;
                result = null;
                return false;
            }

            if (!AreaMusic.TryGetValue(Key(requested), out var value))
                return false;
            if (string.IsNullOrEmpty(value))
                return false;
            var custom = Parse(value);
            if (!NamespaceID.IsValid(custom))
                return false;
            result = custom;
            if (Prepare(result))
                return true;
            result = null;
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Battle music resolve failed: {e}");
            return false;
        }
    }

    // 非音乐室场景：播放前同步确保自定义曲（含 #seq）已解码就绪。
    private static bool Prepare(NamespaceID? id)
    {
        if (id == null)
            return false;
        return MusicPackLoader.EnsureClipSync(id.Path);
    }

    private static string? FindMapArea(NamespaceID requested)
    {
        try
        {
            var main = MainManager.Instance;
            var maps = main.ResourceManager.GetModMapMetas(main.BuiltinNamespace);
            if (maps == null)
                return null;
            var want = Key(requested);
            foreach (var map in maps)
            {
                if (map == null || !NamespaceID.IsValid(map.area) || map.presets == null)
                    continue;
                foreach (var preset in map.presets)
                {
                    if (preset?.music != null && Key(preset.music) == want)
                        return Key(map.area!);
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static int IndexOf(string[] arr, string value)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] == value)
                return i;
        }
        return -1;
    }
}

internal static class BattleMusicRedirect
{
    public static void Apply(NamespaceID id)
    {
        if (id == null)
            return;
        if (BattleMusic.TryResolve(id, out var custom) && custom != null)
        {
            Diag.Log($"[MusicBox] Redirect {BattleMusic.Key(id)} -> {BattleMusic.Key(custom)}");
            BattleMusic.RedirectKey = BattleMusic.Key(id);
            BattleMusic.RedirectId = custom;
        }
    }

    public static void Clear()
    {
        BattleMusic.RedirectKey = null;
        BattleMusic.RedirectId = null;
    }
}

[HarmonyPatch(typeof(MusicManager), nameof(MusicManager.Play))]
internal static class BattleMusicPlayPatch
{
    private static void Prefix(NamespaceID id)
    {
        BattleMusicRedirect.Apply(id);
    }

    private static void Postfix()
    {
        BattleMusicRedirect.Clear();
    }
}

[HarmonyPatch(typeof(MusicManager), nameof(MusicManager.SetPlayingMusic))]
internal static class BattleMusicSetPlayingPatch
{
    private static void Prefix(NamespaceID id)
    {
        BattleMusicRedirect.Apply(id);
    }

    private static void Postfix()
    {
        BattleMusicRedirect.Clear();
    }
}
