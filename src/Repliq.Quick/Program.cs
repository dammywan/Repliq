namespace Repliq.Quick;

using System;
using System.Windows.Forms;
using Repliq.App;
using Repliq.Core.Playback;
using Repliq.Core.Policies;
using Repliq.Core.Recording;
using Repliq.Storage.Workflows;
using Repliq.Windows.Hotkeys;
using Repliq.Windows.Input;
using Repliq.Windows.Recording;

static class Program
{
    /// <summary>
    ///  The main entry point for Repliq Quick — ultra-lightweight standalone Quick Mimic toolbar.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Initialize zero-bloat standalone services
        var workflowRepo = new WorkflowRepository();
        var policyEngine = new DefaultPolicyEngine();
        var automationBackend = new Win32InputBackend();
        var recorderBackend = new Win32RecorderBackend();

        var executionController = new ExecutionController(automationBackend, policyEngine);
        var recordingController = new RecordingController(recorderBackend);
        var hotkeyManager = new GlobalHotkeyManager();

        var quickForm = new QuickPanelForm(
            workflowRepo,
            executionController,
            recordingController,
            historyRepo: null,
            openMainWindowAction: null,
            isStandalone: true);

        // Register default global hotkeys
        // Ctrl+Shift+R = Record/Stop toggle
        // Ctrl+Shift+S = Stop
        // Ctrl+Shift+P = Play
        try
        {
            hotkeyManager.RegisterShortcut("ToggleRecord", "Ctrl+Shift+R", () =>
            {
                if (!quickForm.IsDisposed)
                    quickForm.BeginInvoke(new Action(async () => await quickForm.ToggleRecordingAsync()));
            });

            hotkeyManager.RegisterShortcut("EmergencyStop", "Ctrl+Shift+S", () =>
            {
                if (!quickForm.IsDisposed)
                    quickForm.BeginInvoke(new Action(async () => await quickForm.StopActiveRunAsync()));
            });

            hotkeyManager.RegisterShortcut("Play", "Ctrl+Shift+P", () =>
            {
                if (!quickForm.IsDisposed)
                    quickForm.BeginInvoke(new Action(async () => await quickForm.StartPlaybackAsync()));
            });
        }
        catch
        {
            // Ignore if hotkeys are registered by another process
        }

        quickForm.FormClosed += (_, _) =>
        {
            try { hotkeyManager.Dispose(); } catch { }
        };

        Application.Run(quickForm);
    }
}
