#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MVZ2.Addons;
using MVZ2.IO;
using MVZ2.Localization;
using MVZ2.Managers;
using MVZ2.UI;
using MVZ2.UI.Addons;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MusicBox.Addons;

[HarmonyPatch(typeof(AddonsController), nameof(AddonsController.Awake))]
internal static class AddonsButtonPatch
{
    private static void Postfix(AddonsController __instance)
    {
        try
        {
            MusicPacksPage.Install(__instance);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Install addons button failed: {e}");
        }
    }
}

[HarmonyPatch(typeof(LanguagePacksController), nameof(LanguagePacksController.Awake))]
internal static class LanguagePackAwakeGuard
{
    private static bool Prefix()
    {
        return !MusicPacksPage.CloningMusicPage;
    }
}

[HarmonyPatch(typeof(LanguagePacksController), nameof(LanguagePacksController.OnButtonClickCallback))]
internal static class LanguagePackButtonGuard
{
    private static bool Prefix()
    {
        return !MusicPacksPage.MusicMode;
    }
}

[HarmonyPatch(typeof(LanguagePacksController), nameof(LanguagePacksController.OnPackItemToggledCallback))]
internal static class LanguagePackToggleGuard
{
    private static bool Prefix()
    {
        return !MusicPacksPage.MusicMode;
    }
}

[HarmonyPatch(typeof(LanguagePacksController), nameof(LanguagePacksController.Display))]
internal static class LanguagePackDisplayPatch
{
    private static void Prefix()
    {
        MusicPacksPage.MusicMode = false;
    }
}

internal static class MusicPacksPage
{
    public static bool MusicMode;
    public static bool CloningMusicPage;
    private static AddonsController? addons;
    private static GameObject? pageGo;
    private static LanguagePacksUI? pageUi;
    private static readonly List<string> orderedSongs = new List<string>();
    private static int selectedIndex = -1;

    public static void Install(AddonsController controller)
    {
        addons = controller;
        var ui = controller.ui;
        if (ui == null)
            return;
        var original = ui.languagePackButton;
        if (original == null)
            return;

        var clone = UnityEngine.Object.Instantiate(original.gameObject, original.transform.parent);
        clone.name = "MusicPackButton";
        clone.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
        var tmpLabel = clone.GetComponentInChildren<TMP_Text>();
        if (tmpLabel != null)
        {
            var translator = tmpLabel.GetComponent<TextMeshProUGUITranslator>();
            if (translator != null)
                translator.enabled = false;
            tmpLabel.text = "音乐包";
            Diag.Log("[MusicBox] Music pack button label set via TMP_Text.");
        }
        else
        {
            var uiLabel = clone.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (uiLabel != null)
            {
                uiLabel.text = "音乐包";
                Diag.Log("[MusicBox] Music pack button label set via UI.Text.");
            }
            else
            {
                Plugin.Log.LogWarning("[MusicBox] Music pack button label not found.");
            }
        }
        var button = clone.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.m_OnClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(OpenPage)));
        }
        Diag.Log("[MusicBox] Addons music pack button installed.");
    }

    private static void EnsurePage()
    {
        if (pageGo != null || addons == null)
            return;

        var src = addons.languagePacks.gameObject;
        CloningMusicPage = true;
        try
        {
            pageGo = UnityEngine.Object.Instantiate(src, src.transform.parent);
        }
        finally
        {
            CloningMusicPage = false;
        }
        pageGo.name = "MusicPackPage";
        pageGo.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);

        pageUi = pageGo.GetComponentInChildren<LanguagePacksUI>(true);
        if (pageUi == null)
        {
            Plugin.Log.LogError("[MusicBox] Could not find LanguagePacksUI in cloned page.");
            return;
        }
        pageUi.OnButtonClick = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<LanguagePacksUI.Buttons>>(new Action<LanguagePacksUI.Buttons>(OnButtonClick));
        pageUi.OnPackItemToggled = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool, int, bool>>(new Action<bool, int, bool>(OnItemToggled));

        var ctrl = pageGo.GetComponentInChildren<LanguagePacksController>(true);
        if (ctrl != null)
            UnityEngine.Object.Destroy(ctrl);

        ResetElementList(pageUi.enabledLanguagePacks);
        ResetElementList(pageUi.disabledLanguagePacks);

        if (pageUi.enableButton != null)
            pageUi.enableButton.gameObject.SetActive(false);
        if (pageUi.disableButton != null)
            pageUi.disableButton.gameObject.SetActive(false);

        var disabledGo = FindAncestor(pageUi.disabledLanguagePacks.transform, "Disabled");
        if (disabledGo != null)
            disabledGo.SetActive(false);

        if (pageUi.moveUpButton != null)
        {
            var buttonsGo = FindAncestor(pageUi.moveUpButton.transform, "Buttons");
            if (buttonsGo != null)
            {
                var rect = buttonsGo.GetComponent<RectTransform>();
                if (rect != null)
                {
                    var pos = rect.anchoredPosition;
                    rect.anchoredPosition = new Vector2(-200, pos.y);
                }
            }
        }

        var titleGo = FindDescendant(pageGo.transform, "Title");
        if (titleGo != null)
            SetTmpText(titleGo, "音乐包");

        var enabledGo = FindAncestor(pageUi.enabledLanguagePacks.transform, "Enabled");
        if (enabledGo != null)
        {
            var headerGo = FindChild(enabledGo.transform, "Text");
            if (headerGo != null)
                SetTmpText(headerGo, "已安装");
        }

        ReplaceLanguagePackHints();

        pageGo.SetActive(false);
    }

    private static void OpenPage()
    {
        Diag.Log("[MusicBox] Opening music packs page.");
        MusicMode = true;
        try
        {
            Diag.Log("[MusicBox] OpenPage: calling Reload");
            MusicPackLoader.Reload();
            Diag.Log("[MusicBox] OpenPage: reload done.");
            EnsurePage();
            Diag.Log("[MusicBox] OpenPage: page ready.");
            if (pageGo == null)
                return;
            CloningMusicPage = true;
            try
            {
                pageGo.SetActive(true);
            }
            finally
            {
                CloningMusicPage = false;
            }
            Refresh();
            Diag.Log("[MusicBox] OpenPage: refresh done.");
            addons!.ui.SetIndexVisible(false);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Open music packs page failed: {e}");
        }
    }

    private static void ClosePage()
    {
        MusicMode = false;
        if (pageGo != null)
            pageGo.SetActive(false);
        if (addons != null)
            addons.DisplayIndex();
    }

    private static void Refresh()
    {
        if (pageUi == null)
            return;
        orderedSongs.Clear();
        orderedSongs.AddRange(MusicRegistry.OrderedMainPaths);

        var viewDatas = new Il2CppReferenceArray<LanguagePackViewData>(orderedSongs.Count);
        for (int i = 0; i < orderedSongs.Count; i++)
        {
            var path = orderedSongs[i];
            var meta = MusicRegistry.Metas.TryGetValue(path, out var m) ? m : null;
            var pack = MusicRegistry.SongToPack.TryGetValue(path, out var p) ? p : null;
            var vd = new LanguagePackViewData
            {
                name = meta?.Name ?? path,
            };
            var parts = new List<string>();
            if (pack != null && !string.IsNullOrEmpty(pack.Name))
                parts.Add($"包：{pack.Name}");
            if (meta != null)
            {
                if (!string.IsNullOrEmpty(meta.Author))
                    parts.Add($"作者：{meta.Author}");
                if (!string.IsNullOrEmpty(meta.Source))
                    parts.Add($"来源：{meta.Source}");
            }
            vd.description = string.Join("\n", parts);
            viewDatas[i] = vd;
        }
        pageUi.SetEnabledLanguagePacks(viewDatas);
        pageUi.SetDisabledLanguagePacks(new Il2CppReferenceArray<LanguagePackViewData>(0));
        selectedIndex = -1;
        UpdateButtons();
    }

    private static void UpdateButtons()
    {
        if (pageUi == null)
            return;
        bool selected = selectedIndex >= 0 && selectedIndex < orderedSongs.Count;
        pageUi.SetButtonInteractable(LanguagePacksUI.Buttons.Export, selected);
        pageUi.SetButtonInteractable(LanguagePacksUI.Buttons.Delete, selected);
        pageUi.SetButtonInteractable(LanguagePacksUI.Buttons.MoveUp, selected && selectedIndex > 0);
        pageUi.SetButtonInteractable(LanguagePacksUI.Buttons.MoveDown, selected && selectedIndex < orderedSongs.Count - 1);
        pageUi.SetButtonInteractable(LanguagePacksUI.Buttons.Import, true);
        pageUi.SetButtonInteractable(LanguagePacksUI.Buttons.Return, true);
    }

    private static void OnItemToggled(bool enabled, int index, bool value)
    {
        selectedIndex = value ? index : -1;
        UpdateButtons();
    }

    private static void ResetElementList(ElementList? list)
    {
        if (list == null)
            return;
        var template = list.getTemplate();
        list.itemList.Clear();
        var root = list.ListRoot;
        if (root == null)
            return;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i);
            if (child == null)
                continue;
            var go = child.gameObject;
            if (go == null)
                continue;
            if (template != null && go == template)
                continue;
            UnityEngine.Object.Destroy(go);
        }
    }

    private static GameObject? FindAncestor(Transform start, string name)
    {
        var t = start;
        while (t != null)
        {
            var go = t.gameObject;
            if (go != null && go.name == name)
                return go;
            t = t.parent;
        }
        return null;
    }

    private static GameObject? FindChild(Transform parent, string name)
    {
        int count = parent.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.GetChild(i);
            if (child == null)
                continue;
            var go = child.gameObject;
            if (go != null && go.name == name)
                return go;
        }
        return null;
    }

    private static GameObject? FindDescendant(Transform root, string name)
    {
        int count = root.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.GetChild(i);
            if (child == null)
                continue;
            var go = child.gameObject;
            if (go != null && go.name == name)
                return go;
            var found = FindDescendant(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static void SetTmpText(GameObject root, string text)
    {
        var tmp = root.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null)
            return;
        var translator = tmp.GetComponent<TextMeshProUGUITranslator>();
        if (translator != null)
            translator.enabled = false;
        tmp.text = text;
    }

    private static void ReplaceLanguagePackHints()
    {
        if (pageGo == null)
            return;
        var tmps = pageGo.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            var t = tmps[i];
            if (t == null)
                continue;
            var s = t.text;
            if (string.IsNullOrEmpty(s))
                continue;
            if (s.Contains("语言包") && (s.Contains("更上方") || s.Contains("优先")))
            {
                var translator = t.GetComponent<TextMeshProUGUITranslator>();
                if (translator != null)
                    translator.enabled = false;
                t.text = "更上方的音乐包会在音乐室中更靠前显示。";
            }
        }
    }

    private static void OnButtonClick(LanguagePacksUI.Buttons button)
    {
        try
        {
            switch (button)
            {
                case LanguagePacksUI.Buttons.Return:
                    ClosePage();
                    break;
                case LanguagePacksUI.Buttons.Import:
                    Import();
                    break;
                case LanguagePacksUI.Buttons.Export:
                    Export();
                    break;
                case LanguagePacksUI.Buttons.Delete:
                    Delete();
                    break;
                case LanguagePacksUI.Buttons.MoveUp:
                    Move(-1);
                    break;
                case LanguagePacksUI.Buttons.MoveDown:
                    Move(1);
                    break;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Music packs page action failed: {e}");
        }
    }

    private static void Move(int delta)
    {
        if (selectedIndex < 0)
            return;
        int target = selectedIndex + delta;
        if (target < 0 || target >= orderedSongs.Count)
            return;
        (orderedSongs[selectedIndex], orderedSongs[target]) = (orderedSongs[target], orderedSongs[selectedIndex]);
        MusicRegistry.SetOrder(orderedSongs);
        MusicPackLoader.SaveOrder();
        Refresh();
        selectedIndex = target;
        pageUi?.SelectItemUI(true, target);
        UpdateButtons();
    }

    private static void Import()
    {
        var extensions = new Il2CppStringArray(new[] { "zip" });
        var action = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(new Action<string>(OnImportFile));
        FileHelper.OpenExternalFile(extensions, action);
    }

    private static void OnImportFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            Directory.CreateDirectory(MusicRegistry.BaseDir);
            var dest = Path.Combine(MusicRegistry.BaseDir, Path.GetFileName(path));
            File.Copy(path, dest, true);
            MusicPackLoader.Reload();
            Refresh();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Import music pack failed: {e}");
        }
    }

    private static void Export()
    {
        if (selectedIndex < 0)
            return;
        var pack = MusicRegistry.SongToPack[orderedSongs[selectedIndex]];
        var extensions = new Il2CppStringArray(new[] { "zip" });
        var action = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(new Action<string>(dest => ExportTo(pack, dest)));
        FileHelper.SaveExternalFile(pack.Key, extensions, action);
    }

    private static void ExportTo(MusicPack pack, string dest)
    {
        if (string.IsNullOrEmpty(dest))
            return;
        try
        {
            if (pack.IsZip && File.Exists(pack.SourcePath))
            {
                File.Copy(pack.SourcePath, dest, true);
            }
            else
            {
                MusicPackLoader.CreateZipFromDirectory(pack.RootDir, dest);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Export music pack failed: {e}");
        }
    }

    private static void Delete()
    {
        if (selectedIndex < 0)
            return;
        var path = orderedSongs[selectedIndex];
        if (!MusicRegistry.SongToPack.TryGetValue(path, out var pack))
            return;
        var title = MainManager.Instance.LanguageManager._(MVZ2Logic.Localization.LogicStrings.WARNING);
        var desc = $"是否删除音乐包“{pack.Name}”？";
        var action = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(new Action<bool>(result =>
        {
            if (result)
                DoDelete(pack);
        }));
        MainManager.Instance.Scene.ShowDialogSelect(title, desc, action);
    }

    private static void DoDelete(MusicPack pack)
    {
        try
        {
            if (pack.IsZip)
            {
                if (File.Exists(pack.SourcePath))
                    File.Delete(pack.SourcePath);
            }
            else
            {
                if (Directory.Exists(pack.SourcePath))
                    Directory.Delete(pack.SourcePath, true);
            }
            MusicPackLoader.Reload();
            Refresh();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Delete music pack failed: {e}");
        }
    }
}
