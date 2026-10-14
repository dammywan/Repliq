namespace Repliq.Core.Models;

/// <summary>
/// Strictly closed enumeration of supported declarative action types.
/// Arbitrary code, shell scripts, or plugin calls are strictly forbidden.
/// </summary>
public static class ActionTypes
{
    public const string Wait = "wait";
    public const string TypeText = "typeText";
    public const string Click = "click";
    public const string KeyPress = "keyPress";
    public const string Scroll = "scroll";
    public const string FocusWindow = "focusWindow";
    public const string VerifyElement = "verifyElement";
    public const string Checkpoint = "checkpoint";

    private static readonly HashSet<string> ValidSet = new(StringComparer.OrdinalIgnoreCase)
    {
        Wait,
        TypeText,
        Click,
        KeyPress,
        Scroll,
        FocusWindow,
        VerifyElement,
        Checkpoint
    };

    public static bool IsSupported(string? type)
    {
        return !string.IsNullOrWhiteSpace(type) && ValidSet.Contains(type);
    }

    public static IReadOnlyCollection<string> All => ValidSet;
}
