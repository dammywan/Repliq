using Repliq.Core.Models;
using Repliq.Windows.Input;

namespace Repliq.Integration.Tests;

public class Win32InputKeyPressTests
{
    [Theory]
    [InlineData("Enter", 0x0D)]
    [InlineData("Return", 0x0D)]
    [InlineData("Tab", 0x09)]
    [InlineData("Escape", 0x1B)]
    [InlineData("Esc", 0x1B)]
    [InlineData("Space", 0x20)]
    [InlineData("Back", 0x08)]
    [InlineData("Backspace", 0x08)]
    [InlineData("Delete", 0x2E)]
    [InlineData("Insert", 0x2D)]
    [InlineData("Home", 0x24)]
    [InlineData("End", 0x23)]
    [InlineData("PageUp", 0x21)]
    [InlineData("PageDown", 0x22)]
    [InlineData("Up", 0x26)]
    [InlineData("Down", 0x28)]
    [InlineData("Left", 0x25)]
    [InlineData("Right", 0x27)]
    [InlineData("F1", 0x70)]
    [InlineData("F5", 0x74)]
    [InlineData("F12", 0x7B)]
    [InlineData("A", 0x41)]
    [InlineData("z", 0x5A)]
    [InlineData("D1", 0x31)]
    [InlineData("D9", 0x39)]
    [InlineData("PLUS", 0xBB)]
    [InlineData("+", 0xBB)]
    [InlineData("MINUS", 0xBD)]
    [InlineData("-", 0xBD)]
    [InlineData("MULTIPLY", 0x6A)]
    [InlineData("*", 0x6A)]
    [InlineData("DIVIDE", 0x6F)]
    [InlineData("COMMA", 0xBC)]
    [InlineData("PERIOD", 0xBE)]
    [InlineData("SEMICOLON", 0xBA)]
    [InlineData("NUMPAD0", 0x60)]
    [InlineData("NUMPAD5", 0x65)]
    public void MapKeyToVk_KnownKeys_ReturnsCorrectVk(string key, ushort expectedVk)
    {
        var vk = Win32InputBackend.MapKeyToVk(key);
        Assert.Equal(expectedVk, vk);
    }

    [Theory]
    [InlineData("Ctrl", 0x11)]
    [InlineData("Control", 0x11)]
    [InlineData("Shift", 0x10)]
    [InlineData("Alt", 0x12)]
    [InlineData("Menu", 0x12)]
    [InlineData("Win", 0x5B)]
    [InlineData("Windows", 0x5B)]
    public void MapModifierToVk_KnownModifiers_ReturnsCorrectVk(string modifier, ushort expectedVk)
    {
        var vk = Win32InputBackend.MapModifierToVk(modifier);
        Assert.Equal(expectedVk, vk);
    }

    [Fact]
    public async Task Win32InputBackend_ExecuteKeyPress_Succeeds()
    {
        var backend = new Win32InputBackend();
        var action = new WorkflowAction
        {
            Id = "act-key-1",
            Type = ActionTypes.KeyPress,
            Parameters = new() { ["key"] = "Shift" } // Using Shift so it doesn't disturb foreground
        };

        var result = await backend.ExecuteActionAsync(action, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("act-key-1", result.ActionId);
        Assert.Contains("Shift", result.SafeMessage);
    }

    [Fact]
    public async Task Win32InputBackend_ExecuteKeyPress_WithLowercaseType_Succeeds()
    {
        // Tests the exact scenario from user's bug: Type = "keypress" or "keyPress"
        var backend = new Win32InputBackend();
        var action = new WorkflowAction
        {
            Id = "act-key-2",
            Type = "keyPress", // Mixed case
            Parameters = new() { ["key"] = "Ctrl" }
        };

        var result = await backend.ExecuteActionAsync(action, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.NotEqual("UNSUPPORTED_ACTION", result.ErrorCode);
    }

    [Fact]
    public async Task Win32InputBackend_ExecuteKeyPress_WithModifiers_Succeeds()
    {
        var backend = new Win32InputBackend();
        var action = new WorkflowAction
        {
            Id = "act-key-3",
            Type = ActionTypes.KeyPress,
            Parameters = new()
            {
                ["key"] = "Shift",
                ["modifiers"] = "Ctrl"
            }
        };

        var result = await backend.ExecuteActionAsync(action, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Contains("Ctrl", result.SafeMessage);
    }

    [Fact]
    public async Task Win32InputBackend_ExecuteKeyPress_WithCompoundKey_Succeeds()
    {
        var backend = new Win32InputBackend();
        var action = new WorkflowAction
        {
            Id = "act-key-4",
            Type = ActionTypes.KeyPress,
            Parameters = new()
            {
                ["key"] = "Ctrl+Shift" // Compound string
            }
        };

        var result = await backend.ExecuteActionAsync(action, CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Win32InputBackend_ExecuteKeyPress_WithCompoundKeyEndingInPlus_Succeeds()
    {
        var backend = new Win32InputBackend();
        var action = new WorkflowAction
        {
            Id = "act-key-zoom",
            Type = ActionTypes.KeyPress,
            Parameters = new()
            {
                ["key"] = "Ctrl++" // Zoom shortcut with literal plus
            }
        };

        var result = await backend.ExecuteActionAsync(action, CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Win32InputBackend_ExecuteKeyPress_MissingKey_ReturnsFailure()
    {
        var backend = new Win32InputBackend();
        var action = new WorkflowAction
        {
            Id = "act-key-fail",
            Type = ActionTypes.KeyPress,
            Parameters = new() // No key
        };

        var result = await backend.ExecuteActionAsync(action, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("MISSING_KEY", result.ErrorCode);
    }

    [Fact]
    public async Task Win32InputBackend_ExecuteKeyPress_UnknownKey_ReturnsFailure()
    {
        var backend = new Win32InputBackend();
        var action = new WorkflowAction
        {
            Id = "act-key-fail2",
            Type = ActionTypes.KeyPress,
            Parameters = new() { ["key"] = "NonExistentKey12345" }
        };

        var result = await backend.ExecuteActionAsync(action, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("UNKNOWN_KEY", result.ErrorCode);
    }
}
