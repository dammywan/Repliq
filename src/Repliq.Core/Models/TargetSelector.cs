namespace Repliq.Core.Models;

public sealed class TargetSelector
{
    /// <summary>
    /// Type of selector: "uia" (UI Automation), "coordinate" (screen/window X,Y), or "window".
    /// </summary>
    public string SelectorType { get; set; } = "window";

    public string? ProcessName { get; set; }
    public string? WindowTitle { get; set; }
    public string? AutomationId { get; set; }
    public string? ControlType { get; set; }
    public string? Name { get; set; }
    public string? ClassName { get; set; }

    public int? X { get; set; }
    public int? Y { get; set; }
    public string? RelativeTo { get; set; } = "window"; // "window", "screen", "client"
}

public sealed class ActionVerification
{
    public bool VerifyTargetExists { get; set; } = true;
    public bool VerifyPostCondition { get; set; } = false;
    public string? ExpectedValue { get; set; }
    public int TimeoutMs { get; set; } = 3000;
}
