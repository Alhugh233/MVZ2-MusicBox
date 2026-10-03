#nullable enable

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MVZ2.Managers;
using MVZ2.MusicRoom;
using MVZ2.UI;
using MVZ2.UI.Map;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MusicBox.Battle;

[HarmonyPatch(typeof(MusicRoomController), nameof(MusicRoomController.Awake))]
internal static class BattleMusicButtonPatch
{
    private static void Postfix(MusicRoomController __instance)
    {
        try
        {
            BattleMusicUI.Install(__instance);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Install battle music button failed: {e}");
        }
    }
}

internal static class BattleMusicUI
{
    private static readonly List<string> customPaths = new List<string>();
    private static readonly List<string> customNames = new List<string>();
    private static CustomDialog? dialog;
    private static Action<int>? pendingSelect;

    public static void Install(MusicRoomController controller)
    {
        var ui = controller.ui;
        if (ui == null || ui.returnButton == null)
            return;
        MapUI? mapUi = null;
        var mapUis = Resources.FindObjectsOfTypeAll<MapUI>();
        if (mapUis != null)
        {
            for (int i = 0; i < mapUis.Length; i++)
            {
                if (mapUis[i] != null && mapUis[i].settingButton != null)
                {
                    mapUi = mapUis[i];
                    break;
                }
            }
        }
        if (mapUi == null)
        {
            Plugin.Log.LogWarning("[MusicBox] MapUI.settingButton not found, skip battle music button.");
            return;
        }
        var parent = ui.returnButton.transform.parent;
        var clone = UnityEngine.Object.Instantiate(mapUi.settingButton.gameObject, parent);
        clone.name = "BattleMusicButton";
        var button = clone.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.m_OnClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(Open)));
        }
        Plugin.Log.LogInfo("[MusicBox] Battle music button installed.");
    }

    private static void EnsureDialog()
    {
        if (dialog != null)
            return;
        var all = Resources.FindObjectsOfTypeAll<CustomDialog>();
        CustomDialog? src = null;
        if (all != null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                {
                    src = all[i];
                    break;
                }
            }
        }
        if (src == null)
        {
            Plugin.Log.LogWarning("[MusicBox] CustomDialog not found.");
            return;
        }
        var clone = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
        clone.name = "MusicBoxDialog";
        clone.SetActive(false);
        dialog = clone.GetComponent<CustomDialog>();
    }

    private static void Show(string title, string desc, List<string> options, Action<int> onSelect)
    {
        EnsureDialog();
        if (dialog == null)
            return;
        pendingSelect = onSelect;
        dialog.title.text = title;
        dialog.desc.text = desc;
        int perRow = dialog.countPerRow <= 0 ? 2 : dialog.countPerRow;
        int rowCount = (options.Count + perRow - 1) / perRow;
        var rowList = dialog.buttonRowList;

        var onUpdate = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int, RectTransform>>(
            new Action<int, RectTransform>((i, rect) =>
            {
                var row = rect.GetComponent<ButtonRow>();
                if (row == null)
                    return;
                int start = i * perRow;
                int len = Math.Min(perRow, options.Count - start);
                var texts = new Il2CppStringArray(len);
                for (int k = 0; k < len; k++)
                    texts[k] = options[start + k];
                row.UpdateButtons(texts);
                row.OnButtonClick = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ButtonRow, int>>(
                    new Action<ButtonRow, int>(OnRowClick));
            }));
        rowList.updateList(rowCount, onUpdate, null, null, false, false);

        dialog.gameObject.SetActive(true);
        dialog.ResetPosition();
        var scroll = dialog.GetComponentInChildren<ScrollRect>();
        if (scroll != null)
            scroll.verticalNormalizedPosition = 1f;
    }

    private static void OnRowClick(ButtonRow row, int index)
    {
        if (dialog == null)
            return;
        int perRow = dialog.countPerRow <= 0 ? 2 : dialog.countPerRow;
        int rowIndex = dialog.buttonRowList.indexOf(row);
        int real = rowIndex * perRow + index;
        var sel = pendingSelect;
        pendingSelect = null;
        dialog.gameObject.SetActive(false);
        sel?.Invoke(real);
    }

    private static void BuildCustomList()
    {
        customPaths.Clear();
        customNames.Clear();
        var res = MainManager.Instance.ResourceManager;
        foreach (var path in MusicRegistry.OrderedMainPaths)
        {
            customPaths.Add(path);
            customNames.Add(res.GetMusicName(new PVZEngine.NamespaceID(MusicRegistry.Namespace, path)));
        }
    }

    private static void Open()
    {
        try
        {
            MusicPackLoader.Reload();
            BuildCustomList();
            var chapters = BattleMusic.EnumerateChapters();
            if (chapters.Count == 0)
            {
                Show("战斗音乐", "没有找到可配置的章节。", new List<string> { "确定" }, _ => { });
                return;
            }
            var options = new List<string>();
            foreach (var c in chapters)
                options.Add(c.Name);
            options.Add("取消");
            Show("战斗音乐", "选择章节", options, i =>
            {
                if (i < 0 || i >= chapters.Count)
                    return;
                OpenChapter(chapters[i]);
            });
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Open battle music failed: {e}");
        }
    }

    private static void OpenChapter(BattleMusic.ChapterInfo chapter)
    {
        var options = new List<string>();
        var slotKeys = new List<string>();
        foreach (var bm in chapter.BattleMusics)
        {
            options.Add($"章节音乐（{MainManager.Instance.ResourceManager.GetMusicName(bm)}）：{CurrentBattle(bm)}");
            slotKeys.Add("B|" + BattleMusic.Key(bm));
        }
        foreach (var preset in chapter.MapPresets)
        {
            options.Add($"地图音乐（{preset.Id}）：{CurrentMapPreset(preset.Music)}");
            slotKeys.Add("MAP|" + BattleMusic.Key(preset.Music));
        }
        foreach (var boss in chapter.BossStages)
        {
            var phases = BattleMusic.GetBossPhaseKeys(boss);
            for (int p = 0; p < phases.Length; p++)
            {
                options.Add($"{boss.Path} 阶段{p + 1}：{CurrentStage(boss, p)}");
                slotKeys.Add($"{BattleMusic.Key(boss)}|{p}");
            }
        }
        options.Add("返回");
        Show("战斗音乐", $"{chapter.Name} · 选择要设置的项", options, i =>
        {
            if (i < 0 || i >= slotKeys.Count)
                return;
            var key = slotKeys[i];
            if (key.StartsWith("B|"))
            {
                PickMusic(key.Substring(2), -1, "章节音乐", () => OpenChapter(chapter));
            }
            else if (key.StartsWith("MAP|"))
            {
                PickMusic(key.Substring(4), -2, "地图音乐", () => OpenChapter(chapter));
            }
            else
            {
                var parts = key.Split('|');
                int phase = int.Parse(parts[1]);
                PickMusic(parts[0], phase, $"{parts[0]} 阶段{phase + 1}", () => OpenChapter(chapter));
            }
        });
    }

    private static void PickMusic(string key, int phase, string label, Action onBack)
    {
        var options = new List<string>();
        if (phase == -2)
            options.Add("原版（本章地图音乐）");
        else if (phase < 0)
            options.Add("原版（本章音乐）");
        else
            options.Add("原版（原阶段音乐）");
        options.AddRange(customNames);
        options.Add("返回");
        Show("战斗音乐", $"为「{label}」选择音乐", options, i =>
        {
            if (i == 0)
                Assign(key, phase, null);
            else if (i >= 1 && i - 1 < customPaths.Count)
                Assign(key, phase, customPaths[i - 1]);
            onBack();
        });
    }

    private static void Assign(string key, int phase, string? customPath)
    {
        if (phase == -2)
        {
            if (string.IsNullOrEmpty(customPath))
                BattleMusic.MapMusic.Remove(key);
            else
                BattleMusic.MapMusic[key] = $"{MusicRegistry.Namespace}:{customPath}";
        }
        else if (phase < 0)
        {
            if (string.IsNullOrEmpty(customPath))
                BattleMusic.AreaMusic.Remove(key);
            else
                BattleMusic.AreaMusic[key] = $"{MusicRegistry.Namespace}:{customPath}";
        }
        else
        {
            if (!BattleMusic.StageSlots.TryGetValue(key, out var list))
            {
                list = new List<string>();
                BattleMusic.StageSlots[key] = list;
            }
            while (list.Count <= phase)
                list.Add(string.Empty);
            var phases = BattleMusic.GetBossPhaseKeys(PVZEngine.NamespaceID.Parse(key, MainManager.Instance.BuiltinNamespace)!);
            list[phase] = string.IsNullOrEmpty(customPath)
                ? (phase < phases.Length ? phases[phase] : string.Empty)
                : $"{MusicRegistry.Namespace}:{customPath}";
        }
        BattleMusic.Save();
    }

    private static string CurrentBattle(PVZEngine.NamespaceID music)
    {
        var res = MainManager.Instance.ResourceManager;
        if (BattleMusic.AreaMusic.TryGetValue(BattleMusic.Key(music), out var v) && !string.IsNullOrEmpty(v))
        {
            var id = BattleMusic.Parse(v);
            if (id != null)
                return res.GetMusicName(id);
        }
        return "原版";
    }

    private static string CurrentMapPreset(PVZEngine.NamespaceID music)
    {
        var res = MainManager.Instance.ResourceManager;
        if (BattleMusic.MapMusic.TryGetValue(BattleMusic.Key(music), out var v) && !string.IsNullOrEmpty(v))
        {
            var id = BattleMusic.Parse(v);
            if (id != null)
                return res.GetMusicName(id);
        }
        return res.GetMusicName(music) + "（原版）";
    }

    private static string CurrentStage(PVZEngine.NamespaceID stage, int phase)
    {
        var key = BattleMusic.Key(stage);
        if (BattleMusic.StageSlots.TryGetValue(key, out var list) && phase < list.Count && !string.IsNullOrEmpty(list[phase]))
        {
            var id = BattleMusic.Parse(list[phase]);
            if (id != null)
                return MainManager.Instance.ResourceManager.GetMusicName(id);
        }
        return "原版";
    }
}
