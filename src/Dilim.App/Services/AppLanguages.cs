// The languages Dilim ships (Assets/i18n/<code>.json). The installer (src/Dilim.Setup) links this file, so a language
// added here is offered by Setup too.

namespace Dilim.App.Services;

internal static class AppLanguages
{
    /// <summary>Code → native name (shown in the language pickers of the app and of Setup).</summary>
    public static readonly IReadOnlyList<(string Code, string NativeName)> All =
    [
        ("en", "English"),
        ("tr", "Türkçe"),
        ("es", "Español"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("zh-CN", "简体中文"),
        ("pl", "Polski"),
        ("hi", "हिन्दी"),
        ("ru", "Русский"),
    ];
}
