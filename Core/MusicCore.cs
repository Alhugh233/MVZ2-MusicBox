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
    public string RootDir = string.Empty;
    public string SourcePath = string.Empty;
    public bool IsZip;
    public string Name = string.Empty;
    public string Author = string.Empty;
    public string Description = string.Empty;
    public List<LoadedSong> Songs = new List<LoadedSong>();
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
    public static readonly Dictionary<string, string> ClipFiles = new Dictionary<string, string>();
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

        foreach (var zip in Directory.GetFiles(MusicRegistry.BaseDir, "*.zip"))
        {
            var key = Path.GetFileNameWithoutExtension(zip);
            var pack = TryLoadPack(zip, true, key);
            if (pack != null)
                packs.Add(pack);
        }

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

        // 启动/重载后即后台预解码全部曲目（含双轨 #seq）。
        EnsureClipsDecoded();
    }

    private static MusicPack? TryLoadPack(string sourcePath, bool isZip, string key)
    {
        try
        {
            Diag.Log($"[MusicBox] Load pack '{key}' (zip={isZip})");
            var rootDir = ResolveRootDir(sourcePath, isZip, key);
            if (rootDir == null)
            {
                Plugin.Log.LogError($"[MusicBox] Music pack '{key}': cannot prepare contents.");
                return null;
            }
            Diag.Log($"[MusicBox] Pack '{key}' root: {rootDir}");
            var manifestPath = Path.Combine(rootDir, "pack.json");
            var songsPath = Path.Combine(rootDir, "songs.json");
            if (!File.Exists(manifestPath) || !File.Exists(songsPath))
            {
                Plugin.Log.LogError($"[MusicBox] Music pack '{key}': missing pack.json or songs.json.");
                return null;
            }

            var manifest = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(manifestPath)));
            var songRoot = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(songsPath)));
            var songArray = songRoot != null ? MiniJson.Arr(songRoot.GetValueOrDefault("songs")) : null;
            if (manifest == null || songArray == null)
            {
                Plugin.Log.LogError($"[MusicBox] Music pack '{key}': invalid pack.json or songs.json.");
                return null;
            }

            var pack = new MusicPack
            {
                Key = key,
                RootDir = rootDir,
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
                if (!IsOgg(entry.file))
                {
                    Plugin.Log.LogError($"[MusicBox] Music pack '{key}': song '{entry.id}' file must be .ogg.");
                    return null;
                }
                if (!File.Exists(Path.Combine(rootDir, entry.file)))
                {
                    Plugin.Log.LogError($"[MusicBox] Music pack '{key}': song '{entry.id}' file not found: {entry.file}");
                    return null;
                }
                if (!string.IsNullOrWhiteSpace(entry.subFile))
                {
                    if (!IsOgg(entry.subFile))
                    {
                        Plugin.Log.LogError($"[MusicBox] Music pack '{key}': song '{entry.id}' subFile must be .ogg.");
                        return null;
                    }
                    if (!File.Exists(Path.Combine(rootDir, entry.subFile)))
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
                RegisterClip(mainPath, Path.Combine(rootDir, entry.file));
                if (subPath != null)
                    RegisterClip(subPath, Path.Combine(rootDir, entry.subFile));
            }
            return pack;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Music pack '{key}': {e}");
            return null;
        }
    }

    private static string? ResolveRootDir(string sourcePath, bool isZip, string key)
    {
        if (!isZip)
            return FindPackRoot(sourcePath, 2) ?? sourcePath;
        var extracted = ExtractZip(sourcePath, key);
        return FindPackRoot(extracted, 3) ?? extracted;
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ExtractZip(string sourcePath, string key)
    {
        var dest = Path.Combine(MusicRegistry.CacheDir, key);
        if (Directory.Exists(dest))
            Directory.Delete(dest, true);
        Directory.CreateDirectory(dest);
        ExtractZipNative(sourcePath, dest);
        var root = dest;
        if (!File.Exists(Path.Combine(root, "pack.json")))
        {
            var subs = Directory.GetDirectories(root);
            if (subs.Length == 1 && File.Exists(Path.Combine(subs[0], "pack.json")))
                root = subs[0];
        }
        return root;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ExtractZipNative(string zipPath, string destDir)
    {
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

    private static bool IsOgg(string file)
    {
        return !string.IsNullOrWhiteSpace(file) && file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
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

    private static void RegisterClip(string path, string filePath)
    {
        MusicRegistry.ClipFiles[path] = filePath;
    }

    private sealed class Pcm
    {
        public float[] Data = Array.Empty<float>();
        public int Channels = 2;
        public int SampleRate = 48000;
    }

    private const int PCM_CACHE_MAGIC = 0x4D42504D; // "MBPM"
    private const int PCM_CACHE_VERSION = 1;
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

    // 主线程解析出该 clip 需要的文件路径；后台任务不再访问 ClipFiles，避免并发。
    private static bool ResolveFiles(string path, out string? mainFile, out string? subFile, out bool isSeq)
    {
        mainFile = null;
        subFile = null;
        isSeq = path.EndsWith("#seq");
        if (isSeq)
        {
            var basePath = path.Substring(0, path.Length - 4);
            if (!MusicRegistry.ClipFiles.TryGetValue(basePath, out mainFile))
                return false;
            if (!MusicRegistry.ClipFiles.TryGetValue(basePath + "_sub", out subFile))
                return false;
            return true;
        }
        return MusicRegistry.ClipFiles.TryGetValue(path, out mainFile);
    }

    // 后台异步解码：解出 PCM 后由主线程 Pump 创建 AudioClip。
    public static void EnsureClipAsync(string path)
    {
        if (MusicRegistry.Clips.ContainsKey(path))
            return;
        if (!ResolveFiles(path, out var mainFile, out var subFile, out var isSeq))
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
                pcm = BuildPcm(path, mainFile!, subFile, isSeq);
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
        if (!ResolveFiles(path, out var mainFile, out var subFile, out var isSeq))
            return false;
        try
        {
            var pcm = BuildPcm(path, mainFile!, subFile, isSeq);
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

    private static Pcm? BuildPcm(string path, string mainFile, string? subFile, bool isSeq)
    {
        if (!isSeq)
            return DecodeFilePcm(path, mainFile);
        var basePath = path.Substring(0, path.Length - 4);
        var main = DecodeFilePcm(basePath, mainFile);
        var sub = DecodeFilePcm(basePath + "_sub", subFile!);
        if (main == null)
            return null;
        if (sub == null || sub.Channels != main.Channels || sub.SampleRate != main.SampleRate)
            return main;
        var data = new float[main.Data.Length + sub.Data.Length];
        Array.Copy(main.Data, 0, data, 0, main.Data.Length);
        Array.Copy(sub.Data, 0, data, main.Data.Length, sub.Data.Length);
        return new Pcm { Data = data, Channels = main.Channels, SampleRate = main.SampleRate };
    }

    private static Pcm? DecodeFilePcm(string cacheKey, string file)
    {
        if (TryReadPcmCache(cacheKey, file, out var cached))
            return cached;
        var pcm = DecodePcm(file);
        if (pcm != null)
            WritePcmCache(cacheKey, file, pcm);
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

    private static bool TryReadPcmCache(string path, string file, out Pcm? pcm)
    {
        pcm = null;
        try
        {
            var cachePath = PcmCachePath(path);
            if (!File.Exists(cachePath))
                return false;
            var info = new FileInfo(file);
            if (!info.Exists)
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
            long srcLength = br.ReadInt64();
            long srcTicks = br.ReadInt64();
            if (channels <= 0 || sampleRate <= 0 || sampleCount <= 0)
                return false;
            if (info.Length != srcLength || info.LastWriteTimeUtc.Ticks != srcTicks)
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

    private static void WritePcmCache(string path, string file, Pcm pcm)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists)
                return;
            Directory.CreateDirectory(PcmCacheDir);
            using var fs = File.Create(PcmCachePath(path));
            using var bw = new BinaryWriter(fs);
            bw.Write(PCM_CACHE_MAGIC);
            bw.Write(PCM_CACHE_VERSION);
            bw.Write(pcm.Channels);
            bw.Write(pcm.SampleRate);
            bw.Write(pcm.Data.Length);
            bw.Write(info.Length);
            bw.Write(info.LastWriteTimeUtc.Ticks);
            var bytes = new byte[pcm.Data.Length * 4];
            Buffer.BlockCopy(pcm.Data, 0, bytes, 0, bytes.Length);
            bw.Write(bytes);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Write pcm cache failed {path}: {e}");
        }
    }

    private static Pcm? DecodePcm(string filePath)
    {
        if (DetectOpus(filePath, out int opusChannels))
        {
            Diag.Log($"[MusicBox] Detected Opus, channels={opusChannels}");
            return DecodeOpusPcm(filePath, opusChannels);
        }
        Diag.Log("[MusicBox] Detected Vorbis");
        return DecodeVorbisPcm(filePath);
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

    private static bool DetectOpus(string filePath, out int channels)
    {
        channels = 2;
        try
        {
            using var fs = File.OpenRead(filePath);
            var buf = new byte[65536];
            int n = fs.Read(buf, 0, buf.Length);
            for (int i = 0; i + 10 <= n; i++)
            {
                if (buf[i] == (byte)'O' && buf[i + 1] == (byte)'p' && buf[i + 2] == (byte)'u' && buf[i + 3] == (byte)'s'
                    && buf[i + 4] == (byte)'H' && buf[i + 5] == (byte)'e' && buf[i + 6] == (byte)'a' && buf[i + 7] == (byte)'d')
                {
                    int ch = buf[i + 9];
                    if (ch > 0 && ch <= 8)
                        channels = ch;
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Detect codec failed: {e}");
        }
        return false;
    }

    private static Pcm? DecodeOpusPcm(string filePath, int channels)
    {
        var fileStream = File.OpenRead(filePath);
        var decoder = new OpusDecoder(48000, channels);
        var oggStream = new OpusOggReadStream(decoder, fileStream);
        var samples = new List<short>();
        while (oggStream.HasNextPacket)
        {
            var packet = oggStream.DecodeNextPacket();
            if (packet != null)
                samples.AddRange(packet);
        }
        oggStream.Close();
        fileStream.Dispose();
        if (samples.Count == 0)
            return null;
        int frameCount = samples.Count / channels;
        var data = new float[frameCount * channels];
        for (int i = 0; i < data.Length && i < samples.Count; i++)
            data[i] = samples[i] / 32768f;
        return new Pcm { Data = data, Channels = channels, SampleRate = 48000 };
    }

    private static Pcm? DecodeVorbisPcm(string filePath)
    {
        using var reader = new VorbisReader(filePath);
        int channels = reader.Channels;
        int sampleRate = reader.SampleRate;
        int lengthSamples = (int)reader.TotalSamples;
        if (lengthSamples <= 0 || channels <= 0 || sampleRate <= 0)
            return null;
        var data = new float[lengthSamples * channels];
        int read = 0;
        while (read < data.Length)
        {
            int n = reader.ReadSamples(data, read, data.Length - read);
            if (n <= 0)
                break;
            read += n;
        }
        return new Pcm { Data = data, Channels = channels, SampleRate = sampleRate };
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
