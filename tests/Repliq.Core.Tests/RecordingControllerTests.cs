using Repliq.Core.Models;
using Repliq.Core.Recording;
using Repliq.Core.Testing;

namespace Repliq.Core.Tests;

public class RecordingControllerTests
{
    private readonly MockRecorderBackend _backend = new();

    [Fact]
    public async Task RecordingLifecycle_CapturesAndProducesWorkflow()
    {
        var controller = new RecordingController(_backend);
        await controller.StartAsync();
        Assert.Equal(RecordingState.Recording, controller.State);

        // Simulate user clicking notepad
        _backend.SimulateEvent(new RawRecordedEvent
        {
            EventType = "click",
            X = 150,
            Y = 200,
            Button = "left",
            ProcessName = "notepad.exe",
            WindowTitle = "Untitled - Notepad"
        });

        // Simulate user pressing key
        _backend.SimulateEvent(new RawRecordedEvent
        {
            EventType = "key",
            Key = "A",
            ProcessName = "notepad.exe",
            WindowTitle = "Untitled - Notepad"
        });

        var wf = await controller.StopAsync("Captured Flow");
        Assert.Equal(RecordingState.Review, controller.State);
        Assert.Equal(2, wf.Actions.Count);
        Assert.Equal(ActionTypes.Click, wf.Actions[0].Type);
        Assert.Equal(ActionTypes.KeyPress, wf.Actions[1].Type);
        Assert.Contains("notepad.exe", wf.TargetPolicy.AllowedProcesses);
    }

    [Fact]
    public async Task Recording_FiltersOutRepliqSelfWindow()
    {
        var controller = new RecordingController(_backend);
        await controller.StartAsync();

        // Simulate event in Repliq's own process
        _backend.SimulateEvent(new RawRecordedEvent
        {
            EventType = "click",
            X = 50,
            Y = 50,
            Button = "left",
            ProcessName = "repliq.app.exe",
            WindowTitle = "Repliq"
        });

        var wf = await controller.StopAsync();
        Assert.Empty(wf.Actions); // Excluded to prevent feedback loop
    }

    [Fact]
    public async Task Recording_RedactsSensitiveFields()
    {
        var controller = new RecordingController(_backend);
        await controller.StartAsync();

        _backend.SimulateEvent(new RawRecordedEvent
        {
            EventType = "key",
            Key = "P",
            IsSensitiveField = true
        });

        var wf = await controller.StopAsync();
        Assert.Empty(wf.Actions); // Redacted
        Assert.Equal(1, controller.RedactedEventCount);
    }

    [Fact]
    public async Task Recording_EnforcesMaxEventsLimit()
    {
        var config = new RecordingConfig { MaxEvents = 3 };
        var controller = new RecordingController(_backend, config);
        await controller.StartAsync();

        for (int i = 0; i < 5; i++)
        {
            _backend.SimulateEvent(new RawRecordedEvent
            {
                EventType = "click",
                X = i * 10,
                Y = i * 10,
                Button = "left",
                ProcessName = "notepad.exe"
            });
        }

        // Wait brief moment for background stop
        await Task.Delay(50);
        Assert.True(controller.CapturedEventCount <= 3);
    }
}
