namespace HookshotLauncher.Core;

/// The game's languages, make_game.py's LANGUAGES (--language takes the code); tests/test_launcher_core.py keeps the two the same
public static class Languages
{
    public static readonly IReadOnlyList<(string Code, string Name)> All = new[]
    {
        ("en-US", "English (US)"), ("en-GB", "English (UK)"), ("fr", "French (France)"), ("fr-CA", "French (Canada)"), ("de", "German"),
        ("it", "Italian"), ("es", "Spanish (Spain)"), ("es-419", "Spanish (Latin America)"), ("pt-BR", "Portuguese (Brazil)"), ("nl", "Dutch"),
        ("pl", "Polish"), ("ru", "Russian"), ("ja", "Japanese"), ("ko", "Korean"), ("zh-Hans", "Chinese (Simplified)"),
        ("zh-Hant", "Chinese (Traditional)"),
    };

    /// A code make_game.py takes for a language as saved or handed over: the code itself, else the code of its language alone
    /// ("de-DE" is "de", "fr-FR" "fr"); null when there is none
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();
        foreach (var (c, _) in All)
            if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) return c;
        var language = code.Split('-', '_')[0];
        foreach (var (c, _) in All)
            if (string.Equals(c, language, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    public static string NameOf(string? code) => All.FirstOrDefault(l => l.Code == Normalize(code)).Name ?? "English (US)";
}
