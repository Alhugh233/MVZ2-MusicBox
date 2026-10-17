#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using LanguagePackEnhance;

namespace MusicBox;

/// <summary>
/// 把音乐包里的 <c>languages/&lt;lang&gt;.json</c> 收集成译文并注册给 LPE。
/// 文件结构（按 songId 组织）：
/// <code>
/// {
///   "songId": { "name": "...", "description": "...", "author": "...", "source": "...", "origin": "..." }
/// }
/// </code>
/// 曲目字段对应游戏的 <c>_p</c> context（music.name/description/author/source/origin）。
/// </summary>
internal static class MusicLocalization
{
    public const string Namespace = "MusicBox";

    private static readonly (string Field, string Context)[] Fields =
    {
        ("name", "music.name"),
        ("description", "music.description"),
        ("author", "music.author"),
        ("source", "music.source"),
        ("origin", "music.origin"),
    };

    public static void RegisterAll()
    {
        try
        {
            var entries = new List<TranslationEntry>();
            foreach (var pack in MusicRegistry.Packs.Values)
            {
                CollectPack(pack, entries);
            }
            LanguagePackEnhanceApi.RegisterTranslations(Namespace, entries);
            Diag.Log($"[MusicBox] Registered {entries.Count} music translation(s).");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Register music translations failed: {e}");
        }
    }

    private static void CollectPack(MusicPack pack, List<TranslationEntry> entries)
    {
        foreach (var file in pack.Content.ListJson("languages"))
        {
            var language = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(language))
                continue;

            Dictionary<string, object?>? root;
            try
            {
                var text = pack.Content.ReadText("languages/" + file);
                if (text == null)
                    continue;
                root = MiniJson.Obj(MiniJson.Parse(text));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MusicBox] Invalid language file '{file}': {e}");
                continue;
            }
            if (root == null)
                continue;

            foreach (var song in pack.Songs)
            {
                if (!root.TryGetValue(song.SongId, out var value))
                    continue;
                var fields = MiniJson.Obj(value);
                if (fields == null)
                    continue;

                var entry = song.Entry;
                foreach (var (field, context) in Fields)
                {
                    var source = GetField(entry, field);
                    if (string.IsNullOrEmpty(source))
                        continue;
                    if (!fields.TryGetValue(field, out var translationValue))
                        continue;
                    var translation = translationValue as string;
                    if (string.IsNullOrEmpty(translation))
                        continue;
                    entries.Add(new TranslationEntry(language, source, translation, context));
                }
            }
        }
    }

    private static string GetField(SongEntry entry, string field)
    {
        return field switch
        {
            "name" => entry.name,
            "description" => entry.description,
            "author" => entry.author,
            "source" => entry.source,
            "origin" => entry.origin,
            _ => string.Empty,
        };
    }
}
