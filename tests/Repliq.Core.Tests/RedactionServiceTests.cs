using Repliq.Core.Diagnostics;

namespace Repliq.Core.Tests;

public class RedactionServiceTests
{
    [Theory]
    [InlineData("api_key: AIzaSyD-abc123456789", "api_key=***REDACTED***")]
    [InlineData("token: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", "token=***REDACTED***")]
    [InlineData("password: SuperSecretPassword123!", "password=***REDACTED***")]
    public void RedactText_MasksSensitiveCredentials(string input, string expectedSubstring)
    {
        var redacted = RedactionService.RedactText(input);
        Assert.Contains(expectedSubstring, redacted);
        Assert.DoesNotContain("SuperSecretPassword123!", redacted);
        Assert.DoesNotContain("AIzaSyD-abc123456789", redacted);
    }

    [Fact]
    public void MaskTypedValue_SensitiveTrue_MasksCompletely()
    {
        var masked = RedactionService.MaskTypedValue("secretPassword", isSensitive: true);
        Assert.Equal("••••••••", masked);
    }

    [Fact]
    public void MaskTypedValue_NormalText_MasksInner()
    {
        var masked = RedactionService.MaskTypedValue("John Doe", isSensitive: false);
        Assert.StartsWith("J•••e", masked);
    }

    [Fact]
    public void SanitizeErrorMessage_RedactsUserPathAndSecrets()
    {
        var ex = new Exception("Failed connecting to token: mySecretToken123");
        var sanitized = RedactionService.SanitizeErrorMessage(ex);
        Assert.DoesNotContain("mySecretToken123", sanitized);
        Assert.Contains("***REDACTED***", sanitized);
    }
}
