using System.Text;
using System.Text.Json;
using Repliq.Core.Models;
using Repliq.Core.Policies;
using Repliq.Core.Validation;

namespace Repliq.Integrations.Ai;

public sealed class WorkflowSafetyAnalysis
{
    public bool IsSafeToExecute { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> SecurityRisks { get; set; } = new();
    public List<string> SuggestedImprovements { get; set; } = new();
}

public interface IAiWorkflowAssistant
{
    bool IsConfigured { get; }
    Task<WorkflowSafetyAnalysis> AnalyzeWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition> GenerateWorkflowFromPromptAsync(string prompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight, zero-dependency Gemini API integration adhering to the gemini-api-dev skill guidelines.
/// Uses 'gemini-3.8-flash' directly over HTTP REST with structured JSON responses.
/// All outputs are strictly validated through WorkflowValidator and the PolicyEngine.
/// </summary>
public sealed class GeminiWorkflowAssistant : IAiWorkflowAssistant
{
    private readonly string? _apiKey;
    private readonly HttpClient _httpClient;
    private const string Model = "gemini-3.8-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public GeminiWorkflowAssistant(string? apiKey = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<WorkflowSafetyAnalysis> AnalyzeWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
    {
        var localValidation = WorkflowValidator.Validate(workflow);
        var policyEngine = new DefaultPolicyEngine();

        var analysis = new WorkflowSafetyAnalysis
        {
            IsSafeToExecute = localValidation.IsValid
        };

        foreach (var err in localValidation.Errors)
            analysis.SecurityRisks.Add($"Validation Error: {err}");

        foreach (var warn in localValidation.Warnings)
            analysis.SuggestedImprovements.Add($"Warning: {warn}");

        foreach (var action in workflow.Actions)
        {
            var decision = policyEngine.EvaluateAction(action, workflow);
            if (!decision.IsAllowed)
            {
                analysis.SecurityRisks.Add($"Policy Denial: Action '{action.Type}' ({action.Description}): {decision.Reason}");
                analysis.IsSafeToExecute = false;
            }
        }

        if (!IsConfigured)
        {
            analysis.Summary = analysis.IsSafeToExecute
                ? "Workflow passed local schema and policy checks. (Gemini API key not configured for AI semantic inspection)."
                : "Workflow has local policy or schema violations.";
            return analysis;
        }

        try
        {
            var workflowJson = JsonSerializer.Serialize(workflow, new JsonSerializerOptions { WriteIndented = true });
            var prompt = $@"You are a security auditor for desktop automation workflows in Repliq.
Analyze this workflow JSON for security, target ambiguity, or dangerous actions.
Workflow:
{workflowJson}

Respond ONLY with valid JSON matching this schema:
{{
  ""summary"": ""string"",
  ""additionalRisks"": [""string""],
  ""suggestions"": [""string""]
}}";

            var responseText = await CallGeminiAsync(prompt, cancellationToken);
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                using var doc = JsonDocument.Parse(responseText);
                if (doc.RootElement.TryGetProperty("summary", out var sumElem))
                    analysis.Summary = sumElem.GetString() ?? analysis.Summary;

                if (doc.RootElement.TryGetProperty("additionalRisks", out var risksElem) && risksElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in risksElem.EnumerateArray())
                    {
                        var s = r.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) analysis.SecurityRisks.Add($"AI Audit: {s}");
                    }
                }

                if (doc.RootElement.TryGetProperty("suggestions", out var sugElem) && sugElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sugElem.EnumerateArray())
                    {
                        var str = s.GetString();
                        if (!string.IsNullOrWhiteSpace(str)) analysis.SuggestedImprovements.Add($"AI Suggestion: {str}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            analysis.SuggestedImprovements.Add($"AI semantic check skipped due to network/API error: {ex.Message}");
        }

        return analysis;
    }

    public async Task<WorkflowDefinition> GenerateWorkflowFromPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini API key is not configured.");

        var systemPrompt = $@"You are the Repliq Workflow Assistant. Convert user automation requests into safe, declarative JSON workflows.
Supported action types: {string.Join(", ", ActionTypes.All)}.
Target selector types: 'uia', 'coordinate', 'window'.
Dangerous tools (cmd, powershell, bash, mshta, etc.) are FORBIDDEN.
Do not emit code or scripts. Emit ONLY valid JSON conforming to the Repliq workflow schemaVersion 1:
{{
  ""schemaVersion"": 1,
  ""id"": ""{Guid.NewGuid():N}"",
  ""name"": ""Generated Workflow"",
  ""targetPolicy"": {{
    ""allowedProcesses"": [""notepad.exe""]
  }},
  ""limits"": {{
    ""maxActions"": 50,
    ""maxDurationSeconds"": 120
  }},
  ""actions"": [
    {{
      ""id"": ""step1"",
      ""type"": ""wait"",
      ""delayMs"": 250,
      ""parameters"": {{ ""durationMs"": 250 }}
    }}
  ]
}}";

        var fullPrompt = $"{systemPrompt}\n\nUser Request: {prompt}\n\nRespond ONLY with the JSON object:";
        var responseText = await CallGeminiAsync(fullPrompt, cancellationToken);

        // Sanitize JSON
        var json = responseText.Trim();
        if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            json = json.Substring(7);
        if (json.StartsWith("```"))
            json = json.Substring(3);
        if (json.EndsWith("```"))
            json = json.Substring(0, json.Length - 3);
        json = json.Trim();

        var structCheck = WorkflowValidator.ValidateJsonStructure(json);
        if (!structCheck.IsValid)
            throw new InvalidOperationException("Generated workflow failed JSON validation: " + string.Join("; ", structCheck.Errors));

        var wf = JsonSerializer.Deserialize<WorkflowDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse generated workflow.");

        var modelCheck = WorkflowValidator.Validate(wf);
        if (!modelCheck.IsValid)
            throw new InvalidOperationException("Generated workflow has model validation errors: " + string.Join("; ", modelCheck.Errors));

        return wf;
    }

    private async Task<string> CallGeminiAsync(string prompt, CancellationToken ct)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={_apiKey}";

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                responseMimeType = "application/json"
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var candidate = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return candidate ?? string.Empty;
    }
}
