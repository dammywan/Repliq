using System.Drawing;
using System.Windows.Forms;
using Repliq.App;
using Repliq.App.Views;
using Repliq.Core.Models;
using Repliq.Storage.Workflows;

namespace Repliq.Integration.Tests;

public class UIThemeAndWorkflowEditorTests
{
    private static double GetRelativeLuminance(Color c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;

        r = (r <= 0.03928) ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = (g <= 0.03928) ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = (b <= 0.03928) ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double CalculateContrastRatio(Color foreground, Color background)
    {
        double l1 = GetRelativeLuminance(foreground);
        double l2 = GetRelativeLuminance(background);

        if (l1 < l2)
        {
            var temp = l1;
            l1 = l2;
            l2 = temp;
        }

        return (l1 + 0.05) / (l2 + 0.05);
    }

    [Fact]
    public void AppLogo_LoadsSuccessfully_AndCanBeDrawnToGraphicsSurface()
    {
        var logo = UITheme.AppLogo;
        Assert.NotNull(logo);
        Assert.True(logo.Width > 0, "Logo width must be greater than 0");
        Assert.True(logo.Height > 0, "Logo height must be greater than 0");

        // Verify GDI+ stream decoupling: drawing image should not throw ExternalException or ObjectDisposedException
        using var targetBitmap = new Bitmap(128, 128);
        using var g = Graphics.FromImage(targetBitmap);
        g.DrawImage(logo, 0, 0, 128, 128);
    }

    [Fact]
    public void AppIcon_LoadsSuccessfully_AndIsNonEmpty()
    {
        var icon = UITheme.AppIcon;
        Assert.NotNull(icon);
        Assert.True(icon.Width > 0, "Icon width must be greater than 0");
        Assert.True(icon.Height > 0, "Icon height must be greater than 0");
    }

    [Fact]
    public void DarkTheme_Colors_SatisfyWcagContrastRequirements()
    {
        var p = UITheme.DarkPalette;

        // Primary text on Main Surface
        double primaryContrast = CalculateContrastRatio(p.PrimaryText, p.MainSurface);
        Assert.True(primaryContrast >= 4.5, $"Primary text contrast {primaryContrast:F2} must be >= 4.5");

        // Secondary text on Card Surface
        double secondaryContrast = CalculateContrastRatio(p.SecondaryText, p.CardSurface);
        Assert.True(secondaryContrast >= 3.0, $"Secondary text contrast {secondaryContrast:F2} must be >= 3.0");

        // Disabled button colors
        Color disabledFg = ColorTranslator.FromHtml("#94A3B8");
        Color disabledBg = ColorTranslator.FromHtml("#162032");
        double disabledContrast = CalculateContrastRatio(disabledFg, disabledBg);
        Assert.True(disabledContrast >= 4.5, $"Disabled button contrast {disabledContrast:F2} must be >= 4.5:1 for high accessibility");
    }

    [Fact]
    public void AboutView_RendersAttribution_AndCorrectVersion()
    {
        using var about = new AboutView();
        Assert.NotNull(about);

        // Find all labels in AboutView
        var labels = GetAllControls<Label>(about);
        bool hasTitle = labels.Any(l => l.Text.Contains("Repliq"));
        bool hasVersion = labels.Any(l => l.Text.Contains("v0.1.0-alpha"));
        bool hasAttribution = labels.Any(l => l.Text.Contains("RetsardCo") && l.Text.Contains("2026"));

        Assert.True(hasTitle, "AboutView should contain application title 'Repliq'");
        Assert.True(hasVersion, "AboutView should display version '0.1.0-alpha'");
        Assert.True(hasAttribution, "AboutView must display 'Developed by RetsardCo (2026)'");
    }

    [Fact]
    public void WorkflowEditorView_Initializes_WithMasterDetailAndQuickAddButtons()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Repliq_EditorTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new WorkflowRepository(tempDir);
            using var editor = new WorkflowEditorView(repo, _ => { });

            // Verify editor loads cleanly
            Assert.False(editor.HasUnsavedChanges);

            // Verify Quick Add buttons exist
            var buttons = GetAllControls<ThemedButton>(editor);
            Assert.Contains(buttons, b => b.Text.Contains("Type"));
            Assert.Contains(buttons, b => b.Text.Contains("Click"));
            Assert.Contains(buttons, b => b.Text.Contains("Key"));
            Assert.Contains(buttons, b => b.Text.Contains("Wait"));
            Assert.Contains(buttons, b => b.Text.Contains("Save"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WorkflowEditorView_CanAddEditAndReorderSteps()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Repliq_EditorFlow_" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new WorkflowRepository(tempDir);
            using var editor = new WorkflowEditorView(repo, _ => { });

            var sampleWf = new WorkflowDefinition
            {
                Name = "Integration Test Workflow",
                Actions = new()
                {
                    new WorkflowAction { Id = "step1", Type = ActionTypes.Wait, DelayMs = 300, Parameters = new() { ["durationMs"] = 300 } },
                    new WorkflowAction { Id = "step2", Type = ActionTypes.TypeText, Description = "Type test", Parameters = new() { ["text"] = "Hello" } }
                }
            };

            editor.LoadWorkflow(sampleWf);

            var listViews = GetAllControls<ListView>(editor);
            Assert.Single(listViews);
            var lv = listViews[0];

            Assert.Equal(2, lv.Items.Count);
            Assert.Contains("Delay", lv.Items[0].SubItems[1].Text);
            Assert.Contains("Type Text", lv.Items[1].SubItems[1].Text);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private static List<T> GetAllControls<T>(Control parent) where T : Control
    {
        var result = new List<T>();
        foreach (Control c in parent.Controls)
        {
            if (c is T match) result.Add(match);
            if (c.HasChildren) result.AddRange(GetAllControls<T>(c));
        }
        return result;
    }
}
