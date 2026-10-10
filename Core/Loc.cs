#nullable enable

using LanguagePackEnhance;
using MVZ2.Managers;

namespace MusicBox;

/// <summary>
/// MusicBox 自己界面的本地化辅助。
/// <see cref="T"/> 用于普通文本（走 LPE 的 <c>Translate</c>）；
/// <c>Music*</c> 系列用于曲目元数据（走游戏的 <c>_p</c>，由 LPE 注入的包解析）。
/// </summary>
internal static class Loc
{
    public static string T(string text, params object?[] args)
        => LanguagePackEnhanceApi.Translate(text, args);

    public static string MusicName(string source) => Particular("music.name", source);
    public static string MusicAuthor(string source) => Particular("music.author", source);
    public static string MusicSource(string source) => Particular("music.source", source);
    public static string MusicOrigin(string source) => Particular("music.origin", source);
    public static string MusicDescription(string source) => Particular("music.description", source);

    private static string Particular(string context, string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;
        var languageManager = MainManager.Instance?.LanguageManager;
        if (languageManager == null)
            return source;
        return languageManager._p(context, source);
    }
}
