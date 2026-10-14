using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Repliq.Core.Models;

namespace Repliq.Core.Validation;

public sealed class WorkflowValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public void AddError(string error) => Errors.Add(error);
    public void AddWarning(string warning) => Warnings.Add(warning);
}

public static class WorkflowValidator
{
    public const int MaxJsonSizeBytes = 2 * 1024 * 1024; // 2 MB limit
    public const int MaxAllowedActions = 2000;
    public const int MaxStringLength = 1024;
    public const int MaxDelayMs = 60000; // 1 minute max delay per step
    public const int MaxTimeoutMs = 60000; // 1 minute max timeout
    public const int MaxWorkflowDurationSeconds = 3600; // 1 hour max

    private static readonly HashSet<string> DangerousExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "cmd", "powershell.exe", "powershell", "pwsh.exe", "pwsh",
        "bash.exe", "bash", "wscript.exe", "wscript", "cscript.exe", "cscript",
        "mshta.exe", "mshta", "rundll32.exe", "rundll32", "regsvr32.exe", "regsvr32",
        "certutil.exe", "certutil", "bitsadmin.exe", "bitsadmin", "vssadmin.exe",
        "reg.exe", "reg", "schtasks.exe", "schtasks", "sc.exe", "net.exe"
    };

    private static readonly HashSet<string> KnownActionProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "type", "description", "enabled", "delayms", "timeoutms", "target",
        "parameters", "verification", "requiresconfirmation", "isidempotent"
    };

    private static readonly HashSet<string> KnownWorkflowProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "schemaversion", "id", "name", "description", "createdutc", "modifiedutc",
        "targetpolicy", "limits", "actions", "metadata"
    };

    /// <summary>
    /// Validates raw JSON string prior to deserialization to reject oversized payloads,
    /// malformed syntax, and unauthorized extra root or action properties.
    /// </summary>
    public static WorkflowValidationResult ValidateJsonStructure(string json)
    {
        var result = new WorkflowValidationResult();

        if (string.IsNullOrWhiteSpace(json))
        {
            result.AddError("Workflow JSON cannot be empty or whitespace.");
            return result;
        }

        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxJsonSizeBytes)
        {
            result.AddError($"Workflow JSON exceeds maximum permitted size of {MaxJsonSizeBytes} bytes.");
            return result;
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            result.AddError($"Malformed JSON: {ex.Message}");
            return result;
        }

        if (rootNode is not JsonObject rootObj)
        {
            result.AddError("Workflow JSON root must be an object.");
            return result;
        }

        // Check for unknown root properties
        foreach (var prop in rootObj)
        {
            if (!KnownWorkflowProperties.Contains(prop.Key))
            {
                result.AddError($"Unsupported root property: '{prop.Key}'. Unknown fields are rejected for security.");
            }
        }

        // Validate schemaVersion property
        var svNode = rootObj.FirstOrDefault(kvp => string.Equals(kvp.Key, "schemaVersion", StringComparison.OrdinalIgnoreCase)).Value;
        if (svNode is null)
        {
            result.AddError("Missing required property 'schemaVersion'.");
        }
        else if (svNode.GetValue<int>() != 1)
        {
            result.AddError($"Unsupported schema version '{svNode}'. Only version 1 is supported.");
        }

        // Check actions array structure
        var actionsNode = rootObj.FirstOrDefault(kvp => string.Equals(kvp.Key, "actions", StringComparison.OrdinalIgnoreCase)).Value;
        if (actionsNode is JsonArray actionsArray)
        {
            if (actionsArray.Count > MaxAllowedActions)
            {
                result.AddError($"Workflow contains {actionsArray.Count} actions, exceeding limit of {MaxAllowedActions}.");
            }

            for (int i = 0; i < actionsArray.Count; i++)
            {
                var actionItem = actionsArray[i];
                if (actionItem is not JsonObject actionObj)
                {
                    result.AddError($"Action at index {i} must be an object.");
                    continue;
                }

                foreach (var prop in actionObj)
                {
                    if (!KnownActionProperties.Contains(prop.Key))
                    {
                        result.AddError($"Action[{i}] contains unsupported property '{prop.Key}'. Unknown action fields are rejected.");
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Performs deep structural, semantic, and security validation on a WorkflowDefinition object.
    /// </summary>
    public static WorkflowValidationResult Validate(WorkflowDefinition workflow)
    {
        var result = new WorkflowValidationResult();

        if (workflow is null)
        {
            result.AddError("Workflow object cannot be null.");
            return result;
        }

        if (workflow.SchemaVersion != 1)
        {
            result.AddError($"Unsupported schema version {workflow.SchemaVersion}. Expected version 1.");
        }

        if (string.IsNullOrWhiteSpace(workflow.Id))
        {
            result.AddError("Workflow ID is required.");
        }
        else if (workflow.Id.Length > MaxStringLength)
        {
            result.AddError($"Workflow ID exceeds max length of {MaxStringLength}.");
        }

        if (string.IsNullOrWhiteSpace(workflow.Name))
        {
            result.AddError("Workflow name is required.");
        }
        else if (workflow.Name.Length > MaxStringLength)
        {
            result.AddError($"Workflow name exceeds max length of {MaxStringLength}.");
        }

        if (workflow.Description?.Length > MaxStringLength)
        {
            result.AddError($"Workflow description exceeds max length of {MaxStringLength}.");
        }

        // Validate limits
        if (workflow.Limits is null)
        {
            result.AddError("Workflow limits must be configured.");
        }
        else
        {
            if (workflow.Limits.MaxActions <= 0 || workflow.Limits.MaxActions > MaxAllowedActions)
            {
                result.AddError($"MaxActions limit must be between 1 and {MaxAllowedActions}.");
            }
            if (workflow.Limits.MaxDurationSeconds <= 0 || workflow.Limits.MaxDurationSeconds > MaxWorkflowDurationSeconds)
            {
                result.AddError($"MaxDurationSeconds limit must be between 1 and {MaxWorkflowDurationSeconds}.");
            }
            if (workflow.Limits.MaxActionTimeoutMs <= 0 || workflow.Limits.MaxActionTimeoutMs > MaxTimeoutMs)
            {
                result.AddError($"MaxActionTimeoutMs limit must be between 1 and {MaxTimeoutMs}.");
            }
            if (workflow.Limits.MaxRetries < 0 || workflow.Limits.MaxRetries > 10)
            {
                result.AddError("MaxRetries limit must be between 0 and 10.");
            }
        }

        // Validate target policy
        if (workflow.TargetPolicy is not null)
        {
            foreach (var proc in workflow.TargetPolicy.AllowedProcesses)
            {
                if (DangerousExecutables.Contains(proc))
                {
                    result.AddError($"Process '{proc}' is on the dangerous executable denylist and cannot be targeted.");
                }
            }

            foreach (var domain in workflow.TargetPolicy.AllowedDomains)
            {
                if (!IsValidDomain(domain))
                {
                    result.AddError($"Domain '{domain}' in targetPolicy is not a valid hostname/domain.");
                }
            }

            foreach (var path in workflow.TargetPolicy.AllowedPaths)
            {
                if (HasPathTraversal(path))
                {
                    result.AddError($"Path '{path}' in targetPolicy contains path traversal characters.");
                }
            }
        }

        // Validate actions list
        if (workflow.Actions is null)
        {
            result.AddError("Workflow actions list cannot be null.");
            return result;
        }

        if (workflow.Actions.Count > (workflow.Limits?.MaxActions ?? MaxAllowedActions))
        {
            result.AddError($"Workflow action count ({workflow.Actions.Count}) exceeds permitted limit of {workflow.Limits?.MaxActions}.");
        }

        var seenActionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < workflow.Actions.Count; i++)
        {
            var action = workflow.Actions[i];
            ValidateAction(action, i, seenActionIds, workflow.TargetPolicy, result);
        }

        return result;
    }

    private static void ValidateAction(
        WorkflowAction action,
        int index,
        HashSet<string> seenIds,
        TargetPolicy? policy,
        WorkflowValidationResult result)
    {
        if (action is null)
        {
            result.AddError($"Action at index {index} is null.");
            return;
        }

        if (string.IsNullOrWhiteSpace(action.Id))
        {
            result.AddError($"Action at index {index} has an empty ID.");
        }
        else if (!seenIds.Add(action.Id))
        {
            result.AddError($"Duplicate action ID '{action.Id}' found at index {index}.");
        }

        if (!ActionTypes.IsSupported(action.Type))
        {
            result.AddError($"Action[{index}] has unsupported action type '{action.Type}'. Arbitrary actions are forbidden.");
        }

        if (action.DelayMs < 0 || action.DelayMs > MaxDelayMs)
        {
            result.AddError($"Action[{index}] DelayMs must be between 0 and {MaxDelayMs}.");
        }

        if (action.TimeoutMs <= 0 || action.TimeoutMs > MaxTimeoutMs)
        {
            result.AddError($"Action[{index}] TimeoutMs must be between 1 and {MaxTimeoutMs}.");
        }

        // Action-specific validations
        switch (action.Type?.ToLowerInvariant())
        {
            case ActionTypes.TypeText:
                ValidateTypeTextAction(action, index, result);
                break;
            case ActionTypes.Click:
                ValidateClickAction(action, index, result);
                break;
            case ActionTypes.KeyPress:
                ValidateKeyPressAction(action, index, result);
                break;
            case ActionTypes.Scroll:
                ValidateScrollAction(action, index, result);
                break;
            case ActionTypes.Wait:
                ValidateWaitAction(action, index, result);
                break;
        }

        // Validate target selector
        if (action.Target is not null)
        {
            if (action.Target.ProcessName is not null && DangerousExecutables.Contains(action.Target.ProcessName))
            {
                result.AddError($"Action[{index}] attempts to target dangerous executable '{action.Target.ProcessName}'.");
            }

            if (action.Target.SelectorType is not ("uia" or "coordinate" or "window"))
            {
                result.AddError($"Action[{index}] has invalid SelectorType '{action.Target.SelectorType}'. Must be 'uia', 'coordinate', or 'window'.");
            }

            if (action.Target.SelectorType == "coordinate")
            {
                if (action.Target.X is null || action.Target.Y is null)
                {
                    result.AddError($"Action[{index}] has coordinate selector but X or Y is missing.");
                }
                else if (action.Target.X < -10000 || action.Target.X > 20000 || action.Target.Y < -10000 || action.Target.Y > 20000)
                {
                    result.AddError($"Action[{index}] coordinates ({action.Target.X},{action.Target.Y}) are out of plausible bounds.");
                }

                result.AddWarning($"Action[{index}] uses coordinate playback which is fragile against scaling and layout changes.");
            }
        }
    }

    private static void ValidateTypeTextAction(WorkflowAction action, int index, WorkflowValidationResult result)
    {
        if (action.Parameters.TryGetValue("text", out var textObj))
        {
            var text = textObj?.ToString();
            if (text is not null && text.Length > MaxStringLength)
            {
                result.AddError($"Action[{index}] TypeText text exceeds max length of {MaxStringLength}.");
            }
        }
        else if (!action.Parameters.ContainsKey("variable"))
        {
            result.AddError($"Action[{index}] TypeText must provide either 'text' or 'variable' parameter.");
        }
    }

    private static void ValidateClickAction(WorkflowAction action, int index, WorkflowValidationResult result)
    {
        if (action.Parameters.TryGetValue("button", out var btnObj))
        {
            var btn = btnObj?.ToString()?.ToLowerInvariant();
            if (btn is not ("left" or "right" or "middle" or "double"))
            {
                result.AddError($"Action[{index}] Click button '{btn}' is invalid. Allowed: 'left', 'right', 'middle', 'double'.");
            }
        }
    }

    private static void ValidateKeyPressAction(WorkflowAction action, int index, WorkflowValidationResult result)
    {
        if (!action.Parameters.ContainsKey("key"))
        {
            result.AddError($"Action[{index}] KeyPress must specify 'key' parameter.");
        }
    }

    private static void ValidateScrollAction(WorkflowAction action, int index, WorkflowValidationResult result)
    {
        if (!action.Parameters.ContainsKey("deltaY") && !action.Parameters.ContainsKey("deltaX"))
        {
            result.AddError($"Action[{index}] Scroll must specify 'deltaY' or 'deltaX'.");
        }
    }

    private static void ValidateWaitAction(WorkflowAction action, int index, WorkflowValidationResult result)
    {
        if (action.Parameters.TryGetValue("durationMs", out var durObj))
        {
            if (durObj is JsonElement je && je.TryGetInt32(out var d))
            {
                if (d < 0 || d > MaxDelayMs)
                    result.AddError($"Action[{index}] Wait duration must be between 0 and {MaxDelayMs} ms.");
            }
            else if (int.TryParse(durObj?.ToString(), out var dParsed))
            {
                if (dParsed < 0 || dParsed > MaxDelayMs)
                    result.AddError($"Action[{index}] Wait duration must be between 0 and {MaxDelayMs} ms.");
            }
        }
    }

    public static bool HasPathTraversal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Contains("..")) return true;
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return true;
        return false;
    }

    public static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        if (domain.Contains('/') || domain.Contains('\\') || domain.Contains(':') || domain.Contains('@'))
            return false;
        return Uri.CheckHostName(domain) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }
}
