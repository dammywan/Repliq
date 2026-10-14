using System.Drawing;
using System.Windows.Forms;
using Repliq.App;
using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Policies;
using Repliq.Core.Recording;
using Repliq.Core.Testing;
using Repliq.Storage.Workflows;

namespace Repliq.Integration.Tests;

public class QuickMimicTests
{
    [Fact]
    public void QuickPanelForm_IsCompactAndMinimal()
    {
        var workflowRepo = new WorkflowRepository();
        var mockBackend = new MockAutomationBackend();
        var execController = new ExecutionController(mockBackend);
        var mockRecorder = new MockRecorderBackend();
        var recordingCtrl = new RecordingController(mockRecorder);

        using var panel = new QuickPanelForm(workflowRepo, execController, recordingCtrl, () => { });

        // Compact toolbar verification: height must be small (<= 50px)
        Assert.True(panel.Height <= 50, $"Quick panel height {panel.Height} should be <= 50px for minimal footprint.");
        Assert.True(panel.TopMost, "Quick panel should be pinned/topmost by default.");
        Assert.Equal("Repliq Quick Panel", panel.Text);
    }

    [Fact]
    public async Task QuickMimic_RecordAndPlayUnlimitedTimes_InMemoryOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Repliq_MimicTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var workflowRepo = new WorkflowRepository(tempDir);
            var mockBackend = new MockAutomationBackend();
            var execController = new ExecutionController(mockBackend);
            var mockRecorder = new MockRecorderBackend();
            var recordingCtrl = new RecordingController(mockRecorder);

            using var panel = new QuickPanelForm(workflowRepo, execController, recordingCtrl, () => { });

            // 1. Start recording
            await recordingCtrl.StartAsync();
            mockRecorder.SimulateEvent(new RawRecordedEvent
            {
                EventType = "key",
                Key = "A"
            });
            mockRecorder.SimulateEvent(new RawRecordedEvent
            {
                EventType = "key",
                Key = "B"
            });

            // 2. Stop recording
            var recordedWf = await recordingCtrl.StopAsync();
            Assert.Equal(2, recordedWf.Actions.Count);

            // Set as in-memory quick mimic
            panel.SelectedWorkflow = recordedWf;
            Assert.NotNull(panel.SelectedWorkflow);
            Assert.Equal(2, panel.SelectedWorkflow.Actions.Count);

            // Verify disk repository has 0 workflows (NOT saved to disk)
            var savedInRepo = await workflowRepo.GetAllAsync();
            Assert.Empty(savedInRepo);

            // 3. Play #1
            var record1 = await execController.StartAsync(panel.SelectedWorkflow);
            Assert.Equal(ExecutionState.Completed, record1.FinalState);
            Assert.Equal(2, record1.ExecutedActions);

            // 4. Play #2 (Immediate unlimited replay)
            var record2 = await execController.StartAsync(panel.SelectedWorkflow);
            Assert.Equal(ExecutionState.Completed, record2.FinalState);
            Assert.Equal(2, record2.ExecutedActions);

            // 5. Play #3 (Can play unlimited times)
            var record3 = await execController.StartAsync(panel.SelectedWorkflow);
            Assert.Equal(ExecutionState.Completed, record3.FinalState);
            Assert.Equal(2, record3.ExecutedActions);

            // Repository still has 0 workflows on disk
            savedInRepo = await workflowRepo.GetAllAsync();
            Assert.Empty(savedInRepo);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void QuickPanelForm_StandaloneMode_InitializesCleanly()
    {
        var workflowRepo = new WorkflowRepository();
        var mockBackend = new MockAutomationBackend();
        var execController = new ExecutionController(mockBackend);
        var mockRecorder = new MockRecorderBackend();
        var recordingCtrl = new RecordingController(mockRecorder);

        using var panel = new QuickPanelForm(workflowRepo, execController, recordingCtrl, historyRepo: null, openMainWindowAction: null, isStandalone: true);
        Assert.NotNull(panel);
        Assert.True(panel.Height <= 50);
        Assert.True(panel.ShowInTaskbar);
    }

    [Fact]
    public async Task QuickMimic_RecordViaPanel_StopCompletesMimic_AndPlaysSuccessfully()
    {
        var workflowRepo = new WorkflowRepository();
        var mockBackend = new MockAutomationBackend();
        var execController = new ExecutionController(mockBackend);
        var mockRecorder = new MockRecorderBackend();
        var recordingCtrl = new RecordingController(mockRecorder);

        using var panel = new QuickPanelForm(workflowRepo, execController, recordingCtrl, () => { });

        // User clicks Record on panel
        await panel.ToggleRecordingAsync();
        Assert.Equal(RecordingState.Recording, recordingCtrl.State);

        mockRecorder.SimulateEvent(new RawRecordedEvent { EventType = "key", Key = "Ctrl+C" });
        mockRecorder.SimulateEvent(new RawRecordedEvent { EventType = "key", Key = "Ctrl+V" });

        // User clicks Stop on panel (StopActiveRunAsync)
        await panel.StopActiveRunAsync();
        Assert.NotEqual(RecordingState.Recording, recordingCtrl.State);

        // Mimic is ready immediately in-memory
        Assert.NotNull(panel.QuickMimicWorkflow);
        Assert.Equal(2, panel.QuickMimicWorkflow.Actions.Count);
        Assert.Equal("Quick Mimic", panel.QuickMimicWorkflow.Name);

        // Immediate replay succeeds
        var run = await execController.StartAsync(panel.SelectedWorkflow!);
        Assert.Equal(ExecutionState.Completed, run.FinalState);
        Assert.Equal(2, run.ExecutedActions);
    }

    [Fact]
    public async Task QuickMimic_WhenPanelClosed_InMemoryWorkflowIsDiscarded()
    {
        var workflowRepo = new WorkflowRepository();
        var mockBackend = new MockAutomationBackend();
        var execController = new ExecutionController(mockBackend);
        var mockRecorder = new MockRecorderBackend();
        var recordingCtrl = new RecordingController(mockRecorder);

        var panel = new QuickPanelForm(workflowRepo, execController, recordingCtrl, () => { });

        // Record a quick mimic
        await panel.ToggleRecordingAsync();
        mockRecorder.SimulateEvent(new RawRecordedEvent { EventType = "key", Key = "Enter" });
        await panel.StopRecordingAsync();

        Assert.NotNull(panel.QuickMimicWorkflow);
        Assert.Single(panel.QuickMimicWorkflow.Actions);

        // When closed, the recorded workflow is gone
        panel.Close();
        Assert.Null(panel.QuickMimicWorkflow);
        Assert.Null(panel.SelectedWorkflow);

        panel.Dispose();
    }

    [Fact]
    public async Task RepliqQuick_ExcludedFromRecording_AndBlockedByPolicy()
    {
        // 1. Verify PolicyEngine blocks repliq.quick.exe
        var policyEngine = new DefaultPolicyEngine();
        var target = new TargetSelector { ProcessName = "repliq.quick.exe" };
        var decision = policyEngine.EvaluateTarget(target, null);
        Assert.False(decision.IsAllowed);
        Assert.Equal("DENIED_BLOCKED_PROCESS", decision.ViolationCode);

        // 2. Verify RecordingController excludes repliq.quick.exe events
        var mockRecorder = new MockRecorderBackend();
        var recordingCtrl = new RecordingController(mockRecorder);

        await recordingCtrl.StartAsync();
        mockRecorder.SimulateEvent(new RawRecordedEvent
        {
            EventType = "click",
            ProcessName = "repliq.quick.exe",
            Button = "left",
            X = 100,
            Y = 100
        });
        mockRecorder.SimulateEvent(new RawRecordedEvent
        {
            EventType = "key",
            ProcessName = "notepad.exe",
            Key = "A"
        });

        var wf = await recordingCtrl.StopAsync();
        Assert.Single(wf.Actions); // repliq.quick.exe event was excluded
        Assert.Equal(ActionTypes.KeyPress, wf.Actions[0].Type);
    }

    [Fact]
    public void DarkToolStripRenderer_GetItemTextColor_HighContrastStates()
    {
        var enabledItem = new ToolStripMenuItem("Item 1") { Enabled = true };
        var disabledItem = new ToolStripMenuItem("Item 2") { Enabled = false };

        var normalColor = DarkToolStripRenderer.GetItemTextColor(enabledItem);
        var disabledColor = DarkToolStripRenderer.GetItemTextColor(disabledItem);

        // Enabled text should be crisp light (#F8FAFC)
        Assert.Equal(ColorTranslator.FromHtml("#F8FAFC"), normalColor);

        // Disabled text should be clear muted slate (#64748B), not black
        Assert.Equal(ColorTranslator.FromHtml("#64748B"), disabledColor);
    }

    [Fact]
    public void DarkToolStripRenderer_RendersText_WithHighContrastColors()
    {
        var renderer = new DarkToolStripRenderer();
        using var bmp = new Bitmap(200, 40);
        using var g = Graphics.FromImage(bmp);
        g.Clear(ColorTranslator.FromHtml("#0F172A"));

        var item = new ToolStripMenuItem("Test Text") { Enabled = true };
        var args = new ToolStripItemTextRenderEventArgs(g, item, item.Text, new Rectangle(10, 10, 150, 20), Color.White, item.Font, ContentAlignment.MiddleLeft);
        
        renderer.DrawItemText(args);

        // Check that some non-background pixels were drawn (text was rendered)
        bool hasRenderedPixels = false;
        Color bg = ColorTranslator.FromHtml("#0F172A");
        for (int x = 0; x < bmp.Width; x++)
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                var px = bmp.GetPixel(x, y);
                if (px.ToArgb() != bg.ToArgb())
                {
                    hasRenderedPixels = true;
                    // Ensure the rendered pixel is light (high contrast), not black!
                    Assert.True(px.R > 50 || px.G > 50 || px.B > 50, $"Pixel at ({x},{y}) should not be black on dark background: {px}");
                }
            }
        }
        Assert.True(hasRenderedPixels, "Text should have been rendered to the bitmap.");

        // Now test disabled item
        using var bmpDisabled = new Bitmap(200, 40);
        using var gDisabled = Graphics.FromImage(bmpDisabled);
        gDisabled.Clear(ColorTranslator.FromHtml("#0F172A"));

        var disabledItem = new ToolStripMenuItem("Disabled Text") { Enabled = false };
        var disabledArgs = new ToolStripItemTextRenderEventArgs(gDisabled, disabledItem, disabledItem.Text, new Rectangle(10, 10, 150, 20), Color.White, disabledItem.Font, ContentAlignment.MiddleLeft);
        renderer.DrawItemText(disabledArgs);

        bool hasDisabledPixels = false;
        int maxBrightness = 0;
        Color brightestPixel = Color.Empty;
        for (int x = 0; x < bmpDisabled.Width; x++)
        {
            for (int y = 0; y < bmpDisabled.Height; y++)
            {
                var px = bmpDisabled.GetPixel(x, y);
                if (px.ToArgb() != bg.ToArgb())
                {
                    hasDisabledPixels = true;
                    int b = Math.Max(px.R, Math.Max(px.G, px.B));
                    if (b > maxBrightness)
                    {
                        maxBrightness = b;
                        brightestPixel = px;
                    }
                }
            }
        }
        Assert.True(hasDisabledPixels, "Disabled text should have been rendered to the bitmap.");
        // The disabled text core pixels must be bright/high-contrast (e.g., > 100), not dark/black
        Assert.True(maxBrightness >= 100, $"Brightest disabled pixel was too dark: {brightestPixel}");
    }


    [Fact]
    public void QuickToolbarButton_HighContrastStates_NoIllegibleBlackSquares()
    {
        using var btn = new QuickToolbarButton();

        // 1. When disabled: forecolor must be clear muted silver (#94A3B8), NOT black
        btn.Enabled = false;
        Assert.Equal(ColorTranslator.FromHtml("#94A3B8"), btn.GetCurrentForeColor());
        Assert.Equal(UITheme.Current.CardSurface, btn.GetCurrentBackColor());
        Assert.Equal(UITheme.Current.CardBorder, btn.GetCurrentBorderColor());

        // 2. When enabled (normal): forecolor should be crisp light (#F8FAFC)
        btn.Enabled = true;
        btn.Glyph = ToolbarGlyph.None;
        Assert.Equal(ColorTranslator.FromHtml("#F8FAFC"), btn.GetCurrentForeColor());

        // 3. Play button when enabled should have white text on Accent
        btn.IsPlayButton = true;
        Assert.Equal(Color.White, btn.GetCurrentForeColor());
        Assert.Equal(UITheme.Current.Accent, btn.GetCurrentBackColor());

        // 4. Recording active button should have white text on Error
        btn.IsPlayButton = false;
        btn.IsRecordingActive = true;
        Assert.Equal(Color.White, btn.GetCurrentForeColor());
        Assert.Equal(UITheme.Current.Error, btn.GetCurrentBackColor());

        // 5. Stop button when enabled should have high-contrast red glyph
        btn.IsRecordingActive = false;
        btn.Glyph = ToolbarGlyph.Stop;
        Assert.Equal(ColorTranslator.FromHtml("#EF4444"), btn.GetCurrentForeColor());

        // 6. Resume button when paused should have high-contrast amber glyph
        btn.Glyph = ToolbarGlyph.Resume;
        Assert.Equal(ColorTranslator.FromHtml("#F59E0B"), btn.GetCurrentForeColor());

        // 7. Pause button when enabled should have crisp white glyph
        btn.Glyph = ToolbarGlyph.Pause;
        Assert.Equal(ColorTranslator.FromHtml("#F8FAFC"), btn.GetCurrentForeColor());

        // 8. Options button
        btn.Glyph = ToolbarGlyph.Options;
        Assert.Equal(ColorTranslator.FromHtml("#F8FAFC"), btn.GetCurrentForeColor());

        // 9. Pin button: when pinned forecolor is white, when unpinned muted silver
        btn.Glyph = ToolbarGlyph.Pin;
        btn.IsPinned = true;
        Assert.Equal(Color.White, btn.GetCurrentForeColor());
        Assert.Equal(UITheme.Current.Accent, btn.GetCurrentBackColor());

        btn.IsPinned = false;
        Assert.Equal(ColorTranslator.FromHtml("#94A3B8"), btn.GetCurrentForeColor());
        Assert.Equal(UITheme.Current.CardSurface, btn.GetCurrentBackColor());

        // 10. Close button: light silver normally, red background on hover
        btn.Glyph = ToolbarGlyph.Close;
        Assert.Equal(ColorTranslator.FromHtml("#CBD5E1"), btn.GetCurrentForeColor());

        // 11. Actually paint every glyph to a real Bitmap to verify GDI+ vector rendering executes cleanly
        var glyphsToTest = new[]
        {
            ToolbarGlyph.Pause,
            ToolbarGlyph.Resume,
            ToolbarGlyph.Stop,
            ToolbarGlyph.Options,
            ToolbarGlyph.Pin,
            ToolbarGlyph.Close
        };

        foreach (var glyph in glyphsToTest)
        {
            btn.Glyph = glyph;
            using var bmp = new Bitmap(32, 34);
            using var g = Graphics.FromImage(bmp);
            var pe = new PaintEventArgs(g, new Rectangle(0, 0, 32, 34));
            // Invoke protected OnPaint via reflection
            typeof(QuickToolbarButton)
                .GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(btn, new object[] { pe });

            // Ensure pixels were drawn
            bool painted = false;
            for (int x = 0; x < bmp.Width; x++)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    if (bmp.GetPixel(x, y).ToArgb() != Color.Empty.ToArgb())
                        painted = true;
                }
            }
            Assert.True(painted, $"Glyph {glyph} should paint visible pixels onto bitmap.");
        }
    }

    [Fact]
    public void QuickPanelForm_StandaloneMode_OptionsMenuIsStreamlinedForQuickMimicOnly()
    {
        var workflowRepo = new WorkflowRepository();
        var mockBackend = new MockAutomationBackend();
        var execController = new ExecutionController(mockBackend);
        var mockRecorder = new MockRecorderBackend();
        var recordingCtrl = new RecordingController(mockRecorder);

        using var panel = new QuickPanelForm(
            workflowRepo,
            execController,
            recordingCtrl,
            historyRepo: null,
            openMainWindowAction: null,
            isStandalone: true);

        // Verify standalone window title
        Assert.Equal("Repliq Quick", panel.Text);

        panel.BuildOptionsMenu();
        var items = panel.OptionsMenu.Items.Cast<ToolStripItem>().ToList();

        // Must have Repeat / Loop
        Assert.Contains(items, i => i.Text == "Repeat / Loop");
        var repeatItem = (ToolStripMenuItem)items.First(i => i.Text == "Repeat / Loop");
        Assert.Equal(5, repeatItem.DropDownItems.Count);

        // Must have Playback Speed
        Assert.Contains(items, i => i.Text == "Playback Speed");
        var speedItem = (ToolStripMenuItem)items.First(i => i.Text == "Playback Speed");
        Assert.Equal(4, speedItem.DropDownItems.Count);

        // Must NOT have Select Workflow in standalone mode
        Assert.DoesNotContain(items, i => i.Text == "Select Workflow");

        // Must NOT have Save Mimic in standalone mode
        Assert.DoesNotContain(items, i => i.Text?.Contains("Save Mimic") == true);

        // Must NOT have Open Full Repliq Studio
        Assert.DoesNotContain(items, i => i.Text?.Contains("Open Full Repliq Studio") == true);

        // Must have Close
        Assert.Contains(items, i => i.Text == "✕ Close");

        // Must use DarkToolStripRenderer
        Assert.IsType<DarkToolStripRenderer>(panel.OptionsMenu.Renderer);

        // Must NOT have consecutive separators
        for (int i = 0; i < items.Count - 1; i++)
        {
            Assert.False(items[i] is ToolStripSeparator && items[i + 1] is ToolStripSeparator,
                $"Consecutive separators detected at indices {i} and {i + 1}");
        }
        Assert.False(items.First() is ToolStripSeparator, "First item should not be a separator.");
        Assert.False(items.Last() is ToolStripSeparator, "Last item should not be a separator.");

        // Now test when QuickMimic has recorded actions: Clear Mimic should appear without consecutive separators
        panel.SelectedWorkflow = new WorkflowDefinition
        {
            Name = "Quick Mimic",
            Actions = new List<WorkflowAction> { new() { Type = ActionTypes.KeyPress, Description = "Key" } }
        };
        panel.BuildOptionsMenu();
        var itemsWithMimic = panel.OptionsMenu.Items.Cast<ToolStripItem>().ToList();
        Assert.Contains(itemsWithMimic, i => i.Text == "🗑 Clear Mimic");

        for (int i = 0; i < itemsWithMimic.Count - 1; i++)
        {
            Assert.False(itemsWithMimic[i] is ToolStripSeparator && itemsWithMimic[i + 1] is ToolStripSeparator,
                $"Consecutive separators detected with mimic at indices {i} and {i + 1}");
        }
    }

    [Fact]
    public void QuickPanelForm_NonStandaloneMode_OptionsMenuIncludesLibraryAndStudio()
    {
        var workflowRepo = new WorkflowRepository();
        var mockBackend = new MockAutomationBackend();
        var execController = new ExecutionController(mockBackend);
        var mockRecorder = new MockRecorderBackend();
        var recordingCtrl = new RecordingController(mockRecorder);

        using var panel = new QuickPanelForm(
            workflowRepo,
            execController,
            recordingCtrl,
            historyRepo: null,
            openMainWindowAction: () => { },
            isStandalone: false);

        panel.BuildOptionsMenu();
        var items = panel.OptionsMenu.Items.Cast<ToolStripItem>().ToList();

        // In non-standalone mode, Select Workflow and Open Full Repliq Studio should be available
        Assert.Contains(items, i => i.Text == "Select Workflow");
        Assert.Contains(items, i => i.Text == "↗ Open Full Repliq Studio");

        // Ensure no consecutive separators in non-standalone mode
        for (int i = 0; i < items.Count - 1; i++)
        {
            Assert.False(items[i] is ToolStripSeparator && items[i + 1] is ToolStripSeparator,
                $"Consecutive separators detected in non-standalone at indices {i} and {i + 1}");
        }
    }

    [Fact]
    public void UITheme_ApplyRecursive_StylesControls_WithHighContrastColors()
    {
        using var parentForm = new Form();
        var chk = new CheckBox { Text = "Test Checkbox" };
        var grp = new GroupBox { Text = "Test Group" };
        var nud = new NumericUpDown();
        var lnk = new LinkLabel { Text = "Test Link" };

        parentForm.Controls.AddRange(new Control[] { chk, grp, nud, lnk });

        UITheme.SetTheme("Dark");
        UITheme.Apply(parentForm);

        // Primary text should be high-contrast light
        Assert.Equal(UITheme.DarkPalette.PrimaryText, chk.ForeColor);
        Assert.Equal(UITheme.DarkPalette.PrimaryText, grp.ForeColor);
        Assert.Equal(UITheme.DarkPalette.PrimaryText, nud.ForeColor);
        Assert.Equal(UITheme.DarkPalette.SecondarySurface, nud.BackColor);
        Assert.Equal(UITheme.DarkPalette.Accent, lnk.LinkColor);
    }
}
