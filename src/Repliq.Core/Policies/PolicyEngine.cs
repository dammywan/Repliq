using Repliq.Core.Models;

namespace Repliq.Core.Policies;

public interface IPolicyEngine
{
    PolicyDecision EvaluateAction(WorkflowAction action, WorkflowDefinition workflow);
    PolicyDecision EvaluateTarget(TargetSelector? target, TargetPolicy? workflowPolicy, bool isUntrusted = false);
    bool IsProcessAllowed(string processName, TargetPolicy? workflowPolicy, bool isUntrusted = false);
}

public sealed class SecurityPolicyConfig
{
    public List<string> GlobalAllowedProcesses { get; set; } = new()
    {
        "notepad.exe", "calc.exe", "excel.exe", "chrome.exe", "msedge.exe", "brave.exe",
        "windowsterminal.exe", "wt.exe", "explorer.exe", "taskmgr.exe",
        "antigravity.exe", "code.exe", "devenv.exe"
    };

    public HashSet<string> ApprovedProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> BlockedProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "bash.exe", "wscript.exe", "cscript.exe",
        "mshta.exe", "rundll32.exe", "regsvr32.exe", "certutil.exe", "bitsadmin.exe",
        "reg.exe", "schtasks.exe", "sc.exe", "net.exe", "repliq.app.exe", "repliq.exe",
        "repliq.quick.exe", "repliq.quick"
    };

    public List<string> BlockedWindowPatterns { get; set; } = new()
    {
        "User Account Control",
        "Windows Security",
        "LockApp",
        "Repliq" // Prevent automation feedback loops targeting Repliq itself
    };

    public bool RequireExplicitProcessAllowlist { get; set; } = true;
    public bool RequireConfirmationForSubmits { get; set; } = true;
}

public sealed class DefaultPolicyEngine : IPolicyEngine
{
    private readonly SecurityPolicyConfig _config;

    public DefaultPolicyEngine(SecurityPolicyConfig? config = null)
    {
        _config = config ?? new SecurityPolicyConfig();
    }

    public PolicyDecision EvaluateAction(WorkflowAction action, WorkflowDefinition workflow)
    {
        if (action is null)
            return PolicyDecision.Deny("Action is null.", "NULL_ACTION");

        if (!action.Enabled)
            return PolicyDecision.Allow("Action is disabled.", requiresConfirmation: false);

        if (!ActionTypes.IsSupported(action.Type))
            return PolicyDecision.Deny($"Unsupported action type '{action.Type}'. Default-deny policy applied.", "UNSUPPORTED_ACTION_TYPE");

        // High-impact confirmation checks
        bool requiresConfirmation = action.RequiresConfirmation;
        if (_config.RequireConfirmationForSubmits)
        {
            if (action.Parameters.TryGetValue("isSubmit", out var submitObj) &&
                submitObj is bool isSubmit && isSubmit)
            {
                requiresConfirmation = true;
            }
            if (action.Description?.Contains("delete", StringComparison.OrdinalIgnoreCase) == true ||
                action.Description?.Contains("submit", StringComparison.OrdinalIgnoreCase) == true)
            {
                requiresConfirmation = true;
            }
        }

        // Evaluate target
        if (action.Target is not null)
        {
            bool isUntrusted = workflow.Metadata.TryGetValue("Imported", out var imp) &&
                               string.Equals(imp, "true", StringComparison.OrdinalIgnoreCase);

            var targetDecision = EvaluateTarget(action.Target, workflow.TargetPolicy, isUntrusted);
            if (!targetDecision.IsAllowed)
                return targetDecision;
        }

        return PolicyDecision.Allow("Action approved by policy engine.", requiresConfirmation);
    }

    public PolicyDecision EvaluateTarget(TargetSelector? target, TargetPolicy? workflowPolicy, bool isUntrusted = false)
    {
        if (target is null)
            return PolicyDecision.Allow("Action has no target.", false);

        // Check process against denylist
        if (!string.IsNullOrWhiteSpace(target.ProcessName))
        {
            var proc = target.ProcessName.Trim();
            var fileName = Path.GetFileName(proc);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(proc);
            var nameWithExt = nameWithoutExt + ".exe";

            if (_config.BlockedProcesses.Contains(proc) ||
                _config.BlockedProcesses.Contains(fileName) ||
                _config.BlockedProcesses.Contains(nameWithExt) ||
                _config.BlockedProcesses.Contains(nameWithoutExt))
            {
                return PolicyDecision.Deny(
                    $"Process '{proc}' is on the blocked security denylist and cannot be automated.",
                    "DENIED_BLOCKED_PROCESS");
            }

            if (_config.RequireExplicitProcessAllowlist)
            {
                bool isAllowedGlobally = _config.GlobalAllowedProcesses.Concat(_config.ApprovedProcesses).Any(p =>
                    string.Equals(p, proc, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, fileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, nameWithExt, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, nameWithoutExt, StringComparison.OrdinalIgnoreCase));

                // Imported/untrusted workflows cannot self-authorize processes via their own targetPolicy
                bool isAllowedInWorkflow = !isUntrusted && (workflowPolicy?.AllowedProcesses.Any(p =>
                    string.Equals(p, proc, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, fileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, nameWithExt, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, nameWithoutExt, StringComparison.OrdinalIgnoreCase)) ?? false);

                if (!isAllowedGlobally && !isAllowedInWorkflow)
                {
                    var reason = isUntrusted
                        ? $"Process '{proc}' is not authorized by application policy. Untrusted imported workflows cannot self-authorize target processes."
                        : $"Process '{proc}' is not in the allowed processes policy list.";

                    var code = isUntrusted ? "DENIED_UNTRUSTED_IMPORTED_PROCESS" : "DENIED_PROCESS_NOT_ALLOWLISTED";
                    return PolicyDecision.Deny(reason, code);
                }
            }
        }

        // Check window title against blocked patterns (e.g. UAC, security prompts, Repliq own window)
        if (!string.IsNullOrWhiteSpace(target.WindowTitle))
        {
            foreach (var blocked in _config.BlockedWindowPatterns)
            {
                if (target.WindowTitle.Contains(blocked, StringComparison.OrdinalIgnoreCase))
                {
                    return PolicyDecision.Deny(
                        $"Target window title '{target.WindowTitle}' matches protected window pattern '{blocked}'.",
                        "DENIED_PROTECTED_WINDOW");
                }
            }
        }

        return PolicyDecision.Allow("Target permitted.", false);
    }

    public bool IsProcessAllowed(string processName, TargetPolicy? workflowPolicy, bool isUntrusted = false)
    {
        var dummyTarget = new TargetSelector { ProcessName = processName };
        return EvaluateTarget(dummyTarget, workflowPolicy, isUntrusted).IsAllowed;
    }
}
