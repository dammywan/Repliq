using System.Text.RegularExpressions;

namespace Repliq.Core.Diagnostics;

public static class RedactionService
{
    private static readonly Regex TokenPattern = new(
        @"(api[-_]?key|secret|token|password|bearer|auth|credential)[\s:=]+([^\s,;""']+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    public static string RedactText(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Mask potential API keys, passwords, bearer tokens
        var result = TokenPattern.Replace(input, "$1=***REDACTED***");
        return result;
    }

    public static string MaskTypedValue(string? text, bool isSensitive = false)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (isSensitive)
            return "••••••••";

        if (text.Length <= 4)
            return new string('•', text.Length);

        return $"{text[0]}•••{text[^1]} ({text.Length} chars)";
    }

    public static string SanitizeErrorMessage(Exception ex)
    {
        if (ex is null)
            return "An unknown error occurred.";

        // Avoid exposing internal filesystem paths, memory addresses, connection strings, or full stack traces
        var msg = ex.Message;
        msg = RedactText(msg);

        // Remove absolute user paths if present
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            msg = msg.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        return msg;
    }
}
