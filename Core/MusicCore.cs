#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Concentus.Oggfile;
using Concentus.Structs;
using MVZ2.Metas;
using NLayer;
using NVorbis;
using PVZEngine;
using UnityEngine;

namespace MusicBox;

internal static class Diag
{
    public static void Log(string message)
    {
        try
        {
            var logger = Plugin.Log;
            if (logger != null)
                logger.LogInfo(message);
        }
        catch
        {
        }
    }
}

public class SongEntry
{
    public string id = string.Empty;
    public string name = string.Empty;
    public string file = string.Empty;
    public string subFile = string.Empty;
    public string source = string.Empty;
    public string origin = string.Empty;
    public string author = string.Empty;
    public string description = string.Empty;
}

public class LoadedSong
{
    public string PackKey = string.Empty;
    public string SongId = string.Empty;
    public string MainPath = string.Empty;
    public string? SubPath;
    public SongEntry Entry = new SongEntry();
}

public class MusicPack
{
    public string Key = string.Empty;
    public PackContent Content = new PackContent();
    public string SourcePath = string.Empty;
    public bool IsZip;
    public string Name = string.Empty;
    public string Author = string.Empty;
    public string Description = string.Empty;
    public List<LoadedSong> Songs = new List<LoadedSong>();
}

/// <summary>一条 clip 的来源（文件夹里的文件，或 zip 里的条目）。</summary>
public sealed class ClipSource
{
    public PackContent Content = new PackContent();
    public string RelativePath = string.Empty;
}

/// <summary>
/// 统一的音乐包内容访问：文件夹直接读文件，zip 直接读条目（不再解压到磁盘）。
/// </summary>
public sealed class PackContent
{
    public string SourcePath = string.Empty;
    public bool IsZip;
    public string Prefix = string.Empty;

    public string FullPath(string relativePath)
    {
        return Path.Combine(SourcePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private string EntryName(string relativePath)
    {
        return Prefix + relativePath.Replace('\\', '/');
    }

    public bool Exists(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;
        if (!IsZip)
            return File.Exists(FullPath(relativePath));
        try
        {
            return ZipGetEntry(SourcePath, EntryName(relativePath)) != null;
        }
        catch
        {
            return false;
        }
    }

    public byte[]? ReadBytes(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return null;
        if (!IsZip)
        {
            try
            {
                var full = FullPath(relativePath);
                return File.Exists(full) ? File.ReadAllBytes(full) : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MusicBox] Read '{relativePath}' failed: {e.Message}");
                return null;
            }
        }
        return ZipReadEntry(SourcePath, EntryName(relativePath));
    }

    public string? ReadText(string relativePath)
    {
        var bytes = ReadBytes(relativePath);
        if (bytes == null)
            return null;
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }

    public string[] ListJson(string relativeDir)
    {
        try
        {
            if (!IsZip)
            {
                var dir = FullPath(relativeDir);
                if (!Directory.Exists(dir))
                    return Array.Empty<string>();
                var files = Directory.GetFiles(dir, "*.json");
                var result = new string[files.Length];
                for (int i = 0; i < files.Length; i++)
                    result[i] = Path.GetFileName(files[i]);
                return result;
            }
            var prefix = EntryName(relativeDir);
            if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith("/"))
                prefix += "/";
            var stream = Il2CppSystem.IO.File.OpenRead(SourcePath);
            var archive = new Il2CppSystem.IO.Compression.ZipArchive(stream);
            try
            {
                var result = new List<string>();
                var entries = archive.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var name = entries[i].FullName;
                    if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix) || !name.EndsWith(".json"))
                        continue;
                    var tail = name.Substring(prefix.Length);
                    if (tail.Contains("/"))
                        continue;
                    result.Add(tail);
                }
                return result.ToArray();
            }
            finally
            {
                archive.Dispose();
                stream.Dispose();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] List '{relativeDir}' failed: {e.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>用于 PCM 缓存失效判断；zip 以整个包为准。</summary>
    public long Signature(string relativePath)
    {
        try
        {
            var info = new FileInfo(IsZip ? SourcePath : FullPath(relativePath));
            if (!info.Exists)
                return 0;
            return info.Length * 397L ^ info.LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private static Il2CppSystem.IO.Compression.ZipArchiveEntry? ZipGetEntry(string zipPath, string entryName)
    {
        var stream = Il2CppSystem.IO.File.OpenRead(zipPath);
        var archive = new Il2CppSystem.IO.Compression.ZipArchive(stream);
        try
        {
            return archive.GetEntry(entryName);
        }
        finally
        {
            archive.Dispose();
            stream.Dispose();
        }
    }

    private static byte[]? ZipReadEntry(string zipPath, string entryName)
    {
        try
        {
            var stream = Il2CppSystem.IO.File.OpenRead(zipPath);
            var archive = new Il2CppSystem.IO.Compression.ZipArchive(stream);
            try
            {
                var entry = archive.GetEntry(entryName);
                if (entry == null)
                    return null;
                var entryStream = entry.Open();
                var chunk = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(81920);
                var result = new List<byte>(65536);
                try
                {
                    while (true)
                    {
                        var n = entryStream.Read(chunk, 0, 81920);
                        if (n <= 0)
                            break;
                        for (int i = 0; i < n; i++)
                            result.Add(chunk[i]);
                    }
                }
                finally
                {
                    entryStream.Dispose();
                }
                return result.Count == 0 ? null : result.ToArray();
            }
            finally
            {
                archive.Dispose();
                stream.Dispose();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Read zip entry '{entryName}' failed: {e.Message}");
            return null;
        }
    }

    /// <summary>在 zip 中定位包含 pack.json 的根目录前缀。</summary>
    public static string FindZipPrefix(string zipPath)
    {
        try
        {
            var stream = Il2CppSystem.IO.File.OpenRead(zipPath);
            var archive = new Il2CppSystem.IO.Compression.ZipArchive(stream);
            try
            {
                var best = string.Empty;
                var found = false;
                var entries = archive.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var name = entries[i].FullName;
                    if (string.IsNullOrEmpty(name) || !name.EndsWith("pack.json"))
                        continue;
                    var prefix = name.Substring(0, name.Length - "pack.json".Length);
                    if (prefix.Length > 0 && !prefix.EndsWith("/"))
                        continue;
                    if (!found || prefix.Length < best.Length)
                    {
                        best = prefix;
                        found = true;
                    }
                }
                return found ? best : string.Empty;
            }
            finally
            {
                archive.Dispose();
                stream.Dispose();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Locate pack.json in '{zipPath}' failed: {e.Message}");
            return string.Empty;
        }
    }
}

internal static class MiniJson
{
    public static object? Parse(string text)
    {
        int index = 0;
        return ParseValue(text, ref index);
    }

    private static object? ParseValue(string s, ref int i)
    {
        Skip(s, ref i);
        if (i >= s.Length)
            return null;
        char c = s[i];
        switch (c)
        {
            case '{': return ParseObject(s, ref i);
            case '[': return ParseArray(s, ref i);
            case '"': return ParseString(s, ref i);
            case 't': i += 4; return true;
            case 'f': i += 5; return false;
            case 'n': i += 4; return null;
            default: return ParseNumber(s, ref i);
        }
    }

    private static void Skip(string s, ref int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                i += 2;
                while (i < s.Length && s[i] != '\n')
                    i++;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/'))
                    i++;
                i += 2;
                continue;
            }
            break;
        }
    }

    private static Dictionary<string, object?> ParseObject(string s, ref int i)
    {
        var dict = new Dictionary<string, object?>();
        i++;
        while (i < s.Length)
        {
            Skip(s, ref i);
            if (i >= s.Length || s[i] == '}')
            {
                i++;
                break;
            }
            if (s[i] == ',')
            {
                i++;
                continue;
            }
            var key = ParseString(s, ref i);
            Skip(s, ref i);
            if (i < s.Length && s[i] == ':')
                i++;
            dict[key] = ParseValue(s, ref i);
        }
        return dict;
    }

    private static List<object?> ParseArray(string s, ref int i)
    {
        var list = new List<object?>();
        i++;
        while (i < s.Length)
        {
            Skip(s, ref i);
            if (i >= s.Length || s[i] == ']')
            {
                i++;
                break;
            }
            if (s[i] == ',')
            {
                i++;
                continue;
            }
            list.Add(ParseValue(s, ref i));
        }
        return list;
    }

    private static string ParseString(string s, ref int i)
    {
        var sb = new StringBuilder();
        if (i < s.Length && s[i] == '"')
            i++;
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"')
                break;
            if (c == '\\' && i < s.Length)
            {
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '/': sb.Append('/'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            var hex = s.Substring(i, 4);
                            i += 4;
                            sb.Append((char)Convert.ToInt32(hex, 16));
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static object ParseNumber(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
            i++;
        var sub = s.Substring(start, i - start);
        if (long.TryParse(sub, out var l))
            return l;
        if (double.TryParse(sub, out var d))
            return d;
        return 0L;
    }

    public static Dictionary<string, object?>? Obj(object? o) => o as Dictionary<string, object?>;
    public static List<object?>? Arr(object? o) => o as List<object?>;
    public static string Str(object? o) => o as string ?? string.Empty;
}

internal static class MusicRegistry
{
    public const string Namespace = "musicbox";

    public static string BaseDir => Path.Combine(Application.persistentDataPath, "music_packs");
    public static string CacheDir => Path.Combine(BaseDir, ".cache");
    public static string OrderFilePath => Path.Combine(BaseDir, "music_packs.json");

    public static bool Ready;
    public static readonly List<string> OrderedMainPaths = new List<string>();
    public static readonly Dictionary<string, MusicMeta> Metas = new Dictionary<string, MusicMeta>();
    public static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
    public static readonly Dictionary<string, ClipSource> ClipFiles = new Dictionary<string, ClipSource>();
    public static readonly Dictionary<string, MusicPack> Packs = new Dictionary<string, MusicPack>();
    public static readonly Dictionary<string, MusicPack> SongToPack = new Dictionary<string, MusicPack>();

    public static void Reset()
    {
        Ready = false;
        OrderedMainPaths.Clear();
        Metas.Clear();
        Clips.Clear();
        ClipFiles.Clear();
        Packs.Clear();
        SongToPack.Clear();
    }

    public static NamespaceID[] GetOrderedMainIDs()
    {
        var result = new NamespaceID[OrderedMainPaths.Count];
        for (int i = 0; i < OrderedMainPaths.Count; i++)
        {
            result[i] = new NamespaceID(Namespace, OrderedMainPaths[i]);
        }
        return result;
    }

    public static void SetOrder(IEnumerable<string> mainPaths)
    {
        var valid = new HashSet<string>();
        foreach (var p in OrderedMainPaths)
            valid.Add(p);
        var ordered = new List<string>();
        foreach (var p in mainPaths)
        {
            if (valid.Remove(p))
                ordered.Add(p);
        }
        foreach (var p in OrderedMainPaths)
        {
            if (valid.Remove(p))
                ordered.Add(p);
        }
        OrderedMainPaths.Clear();
        OrderedMainPaths.AddRange(ordered);
    }
}

internal static class MusicPackLoader
{
    private static bool initialized;
    private static bool built;

    public static void Initialize()
    {
        if (initialized)
            return;
        initialized = true;
        Reload();
        Battle.BattleMusic.Load();
    }

    public static void Update()
    {
        if (built)
            MusicRegistry.Ready = true;
    }

    public static void Reload()
    {
        Diag.Log("[MusicBox] Reload begin");
        built = false;

        MusicRegistry.Reset();
        lock (asyncLock)
        {
            pendingDecodes.Clear();
            failedDecodes.Clear();
            completedDecodes.Clear();
        }

        try
        {
            Directory.CreateDirectory(MusicRegistry.BaseDir);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Cannot create music_packs directory: {e}");
            return;
        }

        var packs = new List<MusicPack>();
        Diag.Log($"[MusicBox] Reload base dir: {MusicRegistry.BaseDir}");

        foreach (var dir in Directory.GetDirectories(MusicRegistry.BaseDir))
        {
            var key = Path.GetFileName(dir);
            if (key.StartsWith("."))
                continue;
            var pack = TryLoadPack(dir, false, key);
            if (pack != null)
                packs.Add(pack);
        }

        // 旧 zip 自动解压为目录；运行时不再直读 zip。
        foreach (var zip in Directory.GetFiles(MusicRegistry.BaseDir, "*.zip"))
            MigrateZip(zip);

        var allMainPaths = new List<string>();
        foreach (var pack in packs)
        {
            MusicRegistry.Packs[pack.Key] = pack;
            foreach (var song in pack.Songs)
            {
                MusicRegistry.SongToPack[song.MainPath] = pack;
                allMainPaths.Add(song.MainPath);
            }
        }

        var savedOrder = LoadOrder();
        MusicRegistry.OrderedMainPaths.AddRange(allMainPaths);
        if (savedOrder.Count > 0)
            MusicRegistry.SetOrder(savedOrder);

        built = true;
        MusicRegistry.Ready = true;

        Diag.Log($"[MusicBox] Reload built. {packs.Count} pack(s), {allMainPaths.Count} song(s).");
        Diag.Log($"[MusicBox] Loaded {packs.Count} music pack(s), {allMainPaths.Count} song(s).");

        MusicLocalization.RegisterAll();

        // 清理已被删除的音乐包遗留的缓存。
        PruneCache();

        // 启动/重载后即后台预解码全部曲目（含双轨 #seq）。
        EnsureClipsDecoded();
    }

    private static MusicPack? TryLoadPack(string sourcePath, bool isZip, string key)
    {
        try
        {
            Diag.Log($"[MusicBox] Load pack '{key}' (zip={isZip})");
            var rootDir = isZip ? sourcePath : (FindPackRoot(sourcePath, 2) ?? sourcePath);
            var content = new PackContent
            {
                SourcePath = rootDir,
                IsZip = isZip,
                Prefix = isZip ? PackContent.FindZipPrefix(sourcePath) : string.Empty,
            };
            Diag.Log($"[MusicBox] Pack '{key}' root: {(isZip ? sourcePath + " (zip)" : rootDir)}");
            var manifestText = content.ReadText("pack.json");
            var songsText = content.ReadText("songs.json");
            if (manifestText == null || songsText == null)
            {
                Plugin.Log.LogError($"[MusicBox] Music pack '{key}': missing pack.json or songs.json.");
                return null;
            }

            var manifest = MiniJson.Obj(MiniJson.Parse(manifestText));
            var songRoot = MiniJson.Obj(MiniJson.Parse(songsText));
            var songArray = songRoot != null ? MiniJson.Arr(songRoot.GetValueOrDefault("songs")) : null;
            if (manifest == null || songArray == null)
            {
                Plugin.Log.LogError($"[MusicBox] Music pack '{key}': invalid pack.json or songs.json.");
                return null;
            }

            var pack = new MusicPack
            {
                Key = key,
                Content = content,
                SourcePath = sourcePath,
                IsZip = isZip,
                Name = MiniJson.Str(manifest.GetValueOrDefault("name")),
                Author = MiniJson.Str(manifest.GetValueOrDefault("author")),
                Description = MiniJson.Str(manifest.GetValueOrDefault("description")),
            };

            var ids = new HashSet<string>();
            foreach (var item in songArray)
            {
                var entryObj = MiniJson.Obj(item);
                if (entryObj == null)
                    continue;
                var entry = new SongEntry
                {
                    id = MiniJson.Str(entryObj.GetValueOrDefault("id")),
                    name = MiniJson.Str(entryObj.GetValueOrDefault("name")),
                    file = MiniJson.Str(entryObj.GetValueOrDefault("file")),
                    subFile = MiniJson.Str(entryObj.GetValueOrDefault("subFile")),
                    source = MiniJson.Str(entryObj.GetValueOrDefault("source")),
                    origin = MiniJson.Str(entryObj.GetValueOrDefault("origin")),
                    author = MiniJson.Str(entryObj.GetValueOrDefault("author")),
                    description = MiniJson.Str(entryObj.GetValueOrDefault("description")),
                };
                if (string.IsNullOrWhiteSpace(entry.id))
                {
                    Plugin.Log.LogError($"[MusicBox] Music pack '{key}': a song is missing its id.");
                    return null;
                }
                if (!ids.Add(entry.id))
                {
                    Plugin.Log.LogError($"[MusicBox] Music pack '{key}': duplicate song id '{entry.id}'.");
                    return null;
                }
                if (!IsSupportedAudio(entry.file))
                {
                    Plugin.Log.LogError($"[MusicBox] Music pack '{key}': song '{entry.id}' file must be .ogg, .wav or .mp3.");
                    return null;
                }
                if (!content.Exists(entry.file))
                {
                    Plugin.Log.LogError($"[MusicBox] Music pack '{key}': song '{entry.id}' file not found: {entry.file}");
                    return null;
                }
                if (!string.IsNullOrWhiteSpace(entry.subFile))
                {
                    if (!IsSupportedAudio(entry.subFile))
                    {
                        Plugin.Log.LogError($"[MusicBox] Music pack '{key}': song '{entry.id}' subFile must be .ogg, .wav or .mp3.");
                        return null;
                    }
                    if (!content.Exists(entry.subFile))
                    {
                        Plugin.Log.LogError($"[MusicBox] Music pack '{key}': song '{entry.id}' subFile not found: {entry.subFile}");
                        return null;
                    }
                }

                var mainPath = $"{key}_{entry.id}";
                var subPath = string.IsNullOrWhiteSpace(entry.subFile) ? null : $"{key}_{entry.id}_sub";
                pack.Songs.Add(new LoadedSong
                {
                    PackKey = key,
                    SongId = entry.id,
                    MainPath = mainPath,
                    SubPath = subPath,
                    Entry = entry,
                });

                Diag.Log($"[MusicBox] Song '{entry.id}' validated, creating meta.");
                MusicRegistry.Metas[mainPath] = CreateMeta(mainPath, entry, mainPath, subPath);
                if (subPath != null)
                {
                    MusicRegistry.Metas[mainPath + "#seq"] = CreateMeta(mainPath + "#seq", entry, mainPath + "#seq", null);
                }
                RegisterClip(mainPath, new ClipSource { Content = content, RelativePath = entry.file });
                if (subPath != null)
                    RegisterClip(subPath, new ClipSource { Content = content, RelativePath = entry.subFile });
            }
            return pack;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Music pack '{key}': {e}");
            return null;
        }
    }

    public static void ExtractZipToFolder(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var stream = Il2CppSystem.IO.File.OpenRead(zipPath);
        var archive = new Il2CppSystem.IO.Compression.ZipArchive(stream);
        try
        {
            var entries = archive.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var name = entry.FullName;
                if (string.IsNullOrEmpty(name) || name.EndsWith("/"))
                    continue;
                var destPath = Path.Combine(destDir, name.Replace('/', Path.DirectorySeparatorChar));
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var entryStream = entry.Open();
                var fileStream = Il2CppSystem.IO.File.Open(destPath, Il2CppSystem.IO.FileMode.Create);
                try
                {
                    entryStream.CopyTo(fileStream);
                }
                finally
                {
                    fileStream.Dispose();
                    entryStream.Dispose();
                }
            }
        }
        finally
        {
            archive.Dispose();
            stream.Dispose();
        }
    }

    private static void MigrateZip(string zipPath)
    {
        try
        {
            var key = Path.GetFileNameWithoutExtension(zipPath);
            var dest = Path.Combine(MusicRegistry.BaseDir, key);
            if (Directory.Exists(dest))
            {
                File.Delete(zipPath);
                return;
            }
            Diag.Log($"[MusicBox] Migrating zip pack '{key}' to directory");
            ExtractZipToFolder(zipPath, dest);
            File.Delete(zipPath);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Migrate zip '{zipPath}' failed: {e}");
        }
    }

    private static string? FindPackRoot(string dir, int depth)
    {
        if (File.Exists(Path.Combine(dir, "pack.json")))
            return dir;
        if (depth <= 0)
            return null;
        string[] subs;
        try
        {
            subs = Directory.GetDirectories(dir);
        }
        catch
        {
            return null;
        }
        foreach (var sub in subs)
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith(".") || name == "__MACOSX")
                continue;
            var found = FindPackRoot(sub, depth - 1);
            if (found != null)
                return found;
        }
        return null;
    }

    public static void CreateZipFromDirectory(string sourceDir, string destPath)
    {
        var fs = Il2CppSystem.IO.File.Open(destPath, Il2CppSystem.IO.FileMode.Create);
        var archive = new Il2CppSystem.IO.Compression.ZipArchive(fs, Il2CppSystem.IO.Compression.ZipArchiveMode.Create);
        try
        {
            var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                var entry = archive.CreateEntry(rel);
                var entryStream = entry.Open();
                var bytes = File.ReadAllBytes(file);
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(bytes);
                try
                {
                    entryStream.Write(arr, 0, arr.Length);
                }
                finally
                {
                    entryStream.Dispose();
                }
            }
        }
        finally
        {
            archive.Dispose();
            fs.Dispose();
        }
    }

    private static bool IsSupportedAudio(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return false;
        return file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
    }

    private static MusicMeta CreateMeta(string id, SongEntry entry, string mainPath, string? subPath)
    {
        var meta = new MusicMeta(id);
        meta._Name_k__BackingField = entry.name ?? string.Empty;
        meta._Source_k__BackingField = entry.source ?? string.Empty;
        meta._Origin_k__BackingField = entry.origin ?? string.Empty;
        meta._Author_k__BackingField = entry.author ?? string.Empty;
        meta._Description_k__BackingField = entry.description ?? string.Empty;
        meta._MainTrack_k__BackingField = new NamespaceID(MusicRegistry.Namespace, mainPath);
        if (subPath != null)
            meta._SubTrack_k__BackingField = new NamespaceID(MusicRegistry.Namespace, subPath);
        meta._UnlockConditions_k__BackingField = null;
        return meta;
    }

    private static void RegisterClip(string path, ClipSource source)
    {
        MusicRegistry.ClipFiles[path] = source;
    }

    private sealed class Pcm
    {
        public float[] Data = Array.Empty<float>();
        public int Channels = 2;
        public int SampleRate = 48000;
    }

    private const int PCM_CACHE_MAGIC = 0x4D42504D; // "MBPM"
    private const int PCM_CACHE_VERSION = 2;
    private static readonly object asyncLock = new object();
    private static readonly HashSet<string> pendingDecodes = new HashSet<string>();
    private static readonly HashSet<string> failedDecodes = new HashSet<string>();
    private static readonly List<KeyValuePair<string, Pcm?>> completedDecodes = new List<KeyValuePair<string, Pcm?>>();

    private static string PcmCacheDir => Path.Combine(MusicRegistry.CacheDir, "pcm");

    private static string PcmCachePath(string path)
    {
        return Path.Combine(PcmCacheDir, HashName(path) + ".pcm");
    }

    private static string HashName(string s)
    {
        uint h = 2166136261;
        foreach (char c in s)
        {
            h ^= c;
            h *= 16777619;
        }
        return h.ToString("X8");
    }

    // 清理已被删除的音乐包遗留的缓存（PCM 缓存文件、zip 解压目录）。
    private static void PruneCache()
    {
        try
        {
            // PCM 缓存：只保留当前所有 clip 路径对应的文件。
            var validFiles = new HashSet<string>();
            foreach (var key in MusicRegistry.ClipFiles.Keys)
                validFiles.Add(HashName(key) + ".pcm");
            if (Directory.Exists(PcmCacheDir))
            {
                foreach (var file in Directory.GetFiles(PcmCacheDir, "*.pcm"))
                {
                    if (validFiles.Contains(Path.GetFileName(file)))
                        continue;
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception e)
                    {
                        Diag.Log($"[MusicBox] Cannot delete stale pcm '{file}': {e.Message}");
                    }
                }
            }

            // 旧版 zip 解压缓存已废弃：清理 .cache 下除 pcm 外的遗留目录。
            var cacheDir = MusicRegistry.CacheDir;
            if (Directory.Exists(cacheDir))
            {
                foreach (var dir in Directory.GetDirectories(cacheDir))
                {
                    if (Path.GetFileName(dir) == "pcm")
                        continue;
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (Exception e)
                    {
                        Diag.Log($"[MusicBox] Cannot delete legacy cache dir '{dir}': {e.Message}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Prune cache failed: {e}");
        }
    }

    // 主线程解析出该 clip 需要的文件路径；后台任务不再访问 ClipFiles，避免并发。
    private static bool ResolveFiles(string path, out ClipSource? main, out ClipSource? sub, out bool isSeq)
    {
        main = null;
        sub = null;
        isSeq = path.EndsWith("#seq");
        if (isSeq)
        {
            var basePath = path.Substring(0, path.Length - 4);
            if (!MusicRegistry.ClipFiles.TryGetValue(basePath, out main))
                return false;
            if (!MusicRegistry.ClipFiles.TryGetValue(basePath + "_sub", out sub))
                return false;
            return true;
        }
        return MusicRegistry.ClipFiles.TryGetValue(path, out main);
    }

    // 后台异步解码：解出 PCM 后由主线程 Pump 创建 AudioClip。
    public static void EnsureClipAsync(string path)
    {
        if (MusicRegistry.Clips.ContainsKey(path))
            return;
        if (!ResolveFiles(path, out var mainSource, out var subSource, out var isSeq))
            return;
        lock (asyncLock)
        {
            if (pendingDecodes.Contains(path) || failedDecodes.Contains(path))
                return;
            pendingDecodes.Add(path);
        }
        System.Threading.Tasks.Task.Run(() =>
        {
            Pcm? pcm = null;
            try
            {
                pcm = BuildPcm(path, mainSource!, subSource, isSeq);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MusicBox] Async decode failed {path}: {e}");
            }
            lock (asyncLock)
            {
                completedDecodes.Add(new KeyValuePair<string, Pcm?>(path, pcm));
            }
        });
    }

    // 主线程同步解码：用于非音乐室场景（战斗/地图覆盖），保证播放前就绪。
    public static bool EnsureClipSync(string path)
    {
        if (MusicRegistry.Clips.ContainsKey(path))
            return true;
        if (!ResolveFiles(path, out var mainSource, out var subSource, out var isSeq))
            return false;
        try
        {
            var pcm = BuildPcm(path, mainSource!, subSource, isSeq);
            if (pcm == null)
                return false;
            MusicRegistry.Clips[path] = CreateClip(path, pcm);
            lock (asyncLock) { pendingDecodes.Remove(path); }
            Diag.Log($"[MusicBox] Decoded (sync) {path}");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Sync decode failed {path}: {e}");
            return false;
        }
    }

    private static Pcm? BuildPcm(string path, ClipSource mainSource, ClipSource? subSource, bool isSeq)
    {
        if (!isSeq)
            return DecodeFilePcm(path, mainSource);
        var basePath = path.Substring(0, path.Length - 4);
        var main = DecodeFilePcm(basePath, mainSource);
        var sub = DecodeFilePcm(basePath + "_sub", subSource!);
        if (main == null)
            return null;
        if (sub == null || sub.Channels != main.Channels || sub.SampleRate != main.SampleRate)
        {
            if (sub != null)
                Plugin.Log.LogWarning($"[MusicBox] Sub track ignored for {path}: format mismatch (main {main.Channels}ch/{main.SampleRate}Hz, sub {sub.Channels}ch/{sub.SampleRate}Hz).");
            return main;
        }
        var data = new float[main.Data.Length + sub.Data.Length];
        Array.Copy(main.Data, 0, data, 0, main.Data.Length);
        Array.Copy(sub.Data, 0, data, main.Data.Length, sub.Data.Length);
        return new Pcm { Data = data, Channels = main.Channels, SampleRate = main.SampleRate };
    }

    private static Pcm? DecodeFilePcm(string cacheKey, ClipSource source)
    {
        if (TryReadPcmCache(cacheKey, source, out var cached))
            return cached;
        var bytes = source.Content.ReadBytes(source.RelativePath);
        if (bytes == null)
        {
            Plugin.Log.LogError($"[MusicBox] Cannot read audio '{source.RelativePath}'.");
            return null;
        }
        var pcm = DecodePcm(bytes);
        if (pcm != null)
            WritePcmCache(cacheKey, source, pcm);
        return pcm;
    }

    public static void EnsureClipsDecoded()
    {
        var keys = new List<string>(MusicRegistry.ClipFiles.Keys);
        foreach (var key in keys)
        {
            EnsureClipAsync(key);
            if (!key.EndsWith("_sub") && MusicRegistry.ClipFiles.ContainsKey(key + "_sub"))
                EnsureClipAsync(key + "#seq");
        }
    }

    // 主线程泵：把后台解好的 PCM 变成 AudioClip。
    public static void Pump()
    {
        List<KeyValuePair<string, Pcm?>> done;
        lock (asyncLock)
        {
            if (completedDecodes.Count == 0)
                return;
            done = new List<KeyValuePair<string, Pcm?>>(completedDecodes);
            completedDecodes.Clear();
        }
        bool changed = false;
        foreach (var kv in done)
        {
            lock (asyncLock) { pendingDecodes.Remove(kv.Key); }
            if (kv.Value == null)
            {
                lock (asyncLock) { failedDecodes.Add(kv.Key); }
                Plugin.Log.LogError($"[MusicBox] Failed to decode {kv.Key}");
                continue;
            }
            if (MusicRegistry.Clips.ContainsKey(kv.Key))
                continue;
            try
            {
                MusicRegistry.Clips[kv.Key] = CreateClip(kv.Key, kv.Value);
                changed = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MusicBox] Create clip failed {kv.Key}: {e}");
            }
        }
        if (changed)
            MusicRoomRefresh.Refresh();
    }

    private static bool TryReadPcmCache(string path, ClipSource source, out Pcm? pcm)
    {
        pcm = null;
        try
        {
            var cachePath = PcmCachePath(path);
            if (!File.Exists(cachePath))
                return false;
            var signature = source.Content.Signature(source.RelativePath);
            if (signature == 0)
                return false;
            using var fs = File.OpenRead(cachePath);
            using var br = new BinaryReader(fs);
            if (br.ReadInt32() != PCM_CACHE_MAGIC)
                return false;
            if (br.ReadInt32() != PCM_CACHE_VERSION)
                return false;
            int channels = br.ReadInt32();
            int sampleRate = br.ReadInt32();
            int sampleCount = br.ReadInt32();
            long srcSignature = br.ReadInt64();
            if (channels <= 0 || sampleRate <= 0 || sampleCount <= 0)
                return false;
            if (signature != srcSignature)
                return false;
            var bytes = br.ReadBytes(sampleCount * 4);
            if (bytes.Length != sampleCount * 4)
                return false;
            var data = new float[sampleCount];
            Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
            pcm = new Pcm { Data = data, Channels = channels, SampleRate = sampleRate };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WritePcmCache(string path, ClipSource source, Pcm pcm)
    {
        try
        {
            var signature = source.Content.Signature(source.RelativePath);
            if (signature == 0)
                return;
            Directory.CreateDirectory(PcmCacheDir);
            using var fs = File.Create(PcmCachePath(path));
            using var bw = new BinaryWriter(fs);
            bw.Write(PCM_CACHE_MAGIC);
            bw.Write(PCM_CACHE_VERSION);
            bw.Write(pcm.Channels);
            bw.Write(pcm.SampleRate);
            bw.Write(pcm.Data.Length);
            bw.Write(signature);
            var bytes = new byte[pcm.Data.Length * 4];
            Buffer.BlockCopy(pcm.Data, 0, bytes, 0, bytes.Length);
            bw.Write(bytes);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Write pcm cache failed {path}: {e}");
        }
    }

    private enum AudioFormat
    {
        Unknown,
        Wav,
        Mp3,
    }

    private static Pcm? DecodePcm(byte[] data)
    {
        switch (DetectAudioFormat(data))
        {
            case AudioFormat.Wav:
                Diag.Log("[MusicBox] Detected WAV");
                return DecodeWavPcm(data);
            case AudioFormat.Mp3:
                Diag.Log("[MusicBox] Detected MP3");
                return DecodeMp3Pcm(data);
        }
        if (DetectOpus(data, out int opusChannels))
        {
            Diag.Log($"[MusicBox] Detected Opus, channels={opusChannels}");
            return DecodeOpusPcm(data, opusChannels);
        }
        Diag.Log("[MusicBox] Detected Vorbis");
        return DecodeVorbisPcm(data);
    }

    private static AudioFormat DetectAudioFormat(byte[] data)
    {
        if (data.Length >= 12
            && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
            && data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E')
        {
            return AudioFormat.Wav;
        }
        if (data.Length >= 3 && data[0] == 'I' && data[1] == 'D' && data[2] == '3')
            return AudioFormat.Mp3;
        if (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
            return AudioFormat.Mp3;
        return AudioFormat.Unknown;
    }

    private static AudioClip CreateClip(string name, Pcm pcm)
    {
        int frameCount = pcm.Channels > 0 ? pcm.Data.Length / pcm.Channels : 0;
        if (frameCount <= 0)
            frameCount = 1;
        var clip = AudioClip.Create(name, frameCount, pcm.Channels, pcm.SampleRate, false);
        var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>(pcm.Data);
        clip.SetData(arr, 0);
        return clip;
    }

    private static bool DetectOpus(byte[] data, out int channels)
    {
        channels = 2;
        for (int i = 0; i + 10 <= data.Length; i++)
        {
            if (data[i] == (byte)'O' && data[i + 1] == (byte)'p' && data[i + 2] == (byte)'u' && data[i + 3] == (byte)'s'
                && data[i + 4] == (byte)'H' && data[i + 5] == (byte)'e' && data[i + 6] == (byte)'a' && data[i + 7] == (byte)'d')
            {
                int ch = data[i + 9];
                if (ch > 0 && ch <= 8)
                    channels = ch;
                return true;
            }
        }
        return false;
    }

    private static Pcm? DecodeOpusPcm(byte[] data, int channels)
    {
        using var memory = new MemoryStream(data, false);
        var decoder = new OpusDecoder(48000, channels);
        var oggStream = new OpusOggReadStream(decoder, memory);
        var samples = new List<short>();
        while (oggStream.HasNextPacket)
        {
            var packet = oggStream.DecodeNextPacket();
            if (packet != null)
                samples.AddRange(packet);
        }
        oggStream.Close();
        if (samples.Count == 0)
            return null;
        int frameCount = samples.Count / channels;
        var pcmData = new float[frameCount * channels];
        for (int i = 0; i < pcmData.Length && i < samples.Count; i++)
            pcmData[i] = samples[i] / 32768f;
        return new Pcm { Data = pcmData, Channels = channels, SampleRate = 48000 };
    }

    private static Pcm? DecodeVorbisPcm(byte[] data)
    {
        using var memory = new MemoryStream(data, false);
        using var reader = new VorbisReader(memory);
        int channels = reader.Channels;
        int sampleRate = reader.SampleRate;
        int lengthSamples = (int)reader.TotalSamples;
        if (lengthSamples <= 0 || channels <= 0 || sampleRate <= 0)
            return null;
        var pcmData = new float[lengthSamples * channels];
        int read = 0;
        while (read < pcmData.Length)
        {
            int n = reader.ReadSamples(pcmData, read, pcmData.Length - read);
            if (n <= 0)
                break;
            read += n;
        }
        return new Pcm { Data = pcmData, Channels = channels, SampleRate = sampleRate };
    }

    private static Pcm? DecodeWavPcm(byte[] data)
    {
        try
        {
            using var fs = new MemoryStream(data, false);
            using var reader = new BinaryReader(fs, Encoding.ASCII, true);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
                return null;
            reader.ReadInt32();
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
                return null;

            int format = 0;
            int channels = 0;
            int sampleRate = 0;
            int bits = 0;
            long dataOffset = -1;
            int dataSize = 0;

            while (fs.Position + 8 <= fs.Length)
            {
                var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                var size = reader.ReadInt32();
                if (size < 0)
                    break;
                var next = fs.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    var fmt = reader.ReadBytes(size);
                    if (fmt.Length < 16)
                        return null;
                    format = BitConverter.ToInt16(fmt, 0);
                    channels = BitConverter.ToInt16(fmt, 2);
                    sampleRate = BitConverter.ToInt32(fmt, 4);
                    bits = BitConverter.ToInt16(fmt, 14);
                    if (format == 0xFFFE && fmt.Length >= 26)
                        format = BitConverter.ToInt16(fmt, 24);
                }
                else if (id == "data")
                {
                    dataOffset = fs.Position;
                    dataSize = size;
                }
                if (next > fs.Length)
                    break;
                fs.Position = next;
            }

            if (format != 1 && format != 3)
                return null;
            if (dataOffset < 0 || dataSize <= 0 || channels <= 0 || sampleRate <= 0)
                return null;
            var bytesPerSample = bits / 8;
            if (bytesPerSample <= 0)
                return null;
            if (format == 3 && bits != 32 && bits != 64)
                return null;

            fs.Position = dataOffset;
            var raw = reader.ReadBytes(dataSize);
            var frameCount = raw.Length / (bytesPerSample * channels);
            if (frameCount <= 0)
                return null;

            var pcmData = new float[frameCount * channels];
            for (int i = 0; i < pcmData.Length; i++)
            {
                var offset = i * bytesPerSample;
                if (format == 3)
                {
                    pcmData[i] = bits == 64
                        ? (float)BitConverter.ToDouble(raw, offset)
                        : BitConverter.ToSingle(raw, offset);
                }
                else
                {
                    switch (bits)
                    {
                        case 8:
                            pcmData[i] = (raw[offset] - 128) / 128f;
                            break;
                        case 16:
                            pcmData[i] = BitConverter.ToInt16(raw, offset) / 32768f;
                            break;
                        case 24:
                            pcmData[i] = Read24Bit(raw, offset) / 8388608f;
                            break;
                        case 32:
                            pcmData[i] = BitConverter.ToInt32(raw, offset) / 2147483648f;
                            break;
                        default:
                            return null;
                    }
                }
            }
            return new Pcm { Data = pcmData, Channels = channels, SampleRate = sampleRate };
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Decode WAV failed: {e}");
            return null;
        }
    }

    private static int Read24Bit(byte[] raw, int offset)
    {
        var value = raw[offset] | (raw[offset + 1] << 8) | (raw[offset + 2] << 16);
        if ((value & 0x800000) != 0)
            value |= unchecked((int)0xFF000000);
        return value;
    }

    private static Pcm? DecodeMp3Pcm(byte[] data)
    {
        try
        {
            using var memory = new MemoryStream(data, false);
            using var reader = new MpegFile(memory);
            var channels = reader.Channels;
            var sampleRate = reader.SampleRate;
            if (channels <= 0 || sampleRate <= 0)
                return null;

            var estimated = reader.Length / 4;
            if (estimated <= 0)
                estimated = 1024L * channels;
            if (estimated > int.MaxValue / 2)
                estimated = int.MaxValue / 2;

            var pcmData = new float[(int)estimated];
            var read = 0;
            while (true)
            {
                if (read == pcmData.Length)
                    Array.Resize(ref pcmData, pcmData.Length * 2);
                var n = reader.ReadSamples(pcmData, read, pcmData.Length - read);
                if (n <= 0)
                    break;
                read += n;
            }
            if (read <= 0)
                return null;
            if (read < pcmData.Length)
                Array.Resize(ref pcmData, read);
            return new Pcm { Data = pcmData, Channels = channels, SampleRate = sampleRate };
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Decode MP3 failed: {e}");
            return null;
        }
    }

    public static List<string> LoadOrder()
    {
        try
        {
            if (File.Exists(MusicRegistry.OrderFilePath))
            {
                var obj = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(MusicRegistry.OrderFilePath)));
                var arr = obj != null ? MiniJson.Arr(obj.GetValueOrDefault("order")) : null;
                if (arr != null)
                {
                    var list = new List<string>();
                    foreach (var item in arr)
                        list.Add(MiniJson.Str(item));
                    return list;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Failed to read order file: {e}");
        }
        return new List<string>();
    }

    public static void SaveOrder()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("{\n    \"order\": [");
            for (int i = 0; i < MusicRegistry.OrderedMainPaths.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append('"').Append(Escape(MusicRegistry.OrderedMainPaths[i])).Append('"');
            }
            sb.Append("]\n}\n");
            File.WriteAllText(MusicRegistry.OrderFilePath, sb.ToString());
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Failed to write order file: {e}");
        }
    }

    private static string Escape(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

// 解码完成后，若音乐室正在显示对应曲目，刷新其信息（例如把 ??：?? 更新为真实时长）。
internal static class MusicRoomRefresh
{
    public static void Refresh()
    {
        try
        {
            var rooms = Resources.FindObjectsOfTypeAll<MVZ2.MusicRoom.MusicRoomController>();
            if (rooms == null)
                return;
            for (int i = 0; i < rooms.Length; i++)
            {
                var room = rooms[i];
                if (room == null || room.gameObject == null || !room.gameObject.activeInHierarchy)
                    continue;
                var list = room.musicList;
                if (list == null || list.Count == 0)
                    continue;
                var cur = room.currentMusicId;
                if (cur == null)
                    continue;
                int index = list.IndexOf(cur);
                if (index < 0)
                    continue;
                room.DisplayMusic(index);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Music room refresh failed: {e}");
        }
    }
}
