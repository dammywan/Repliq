using System.Diagnostics;
using System.Windows.Automation;
using Repliq.Core.Models;

namespace Repliq.Windows.UIAutomation;

public interface IUiaAutomationAdapter
{
    Task<AutomationElement?> FindElementAsync(TargetSelector target, int timeoutMs = 3000, CancellationToken cancellationToken = default);
    Task<bool> VerifyElementAsync(TargetSelector target, int timeoutMs = 3000, CancellationToken cancellationToken = default);
    Task<bool> TryInvokeElementAsync(TargetSelector target, int timeoutMs = 3000, CancellationToken cancellationToken = default);
    Task<bool> TrySetElementValueAsync(TargetSelector target, string value, int timeoutMs = 3000, CancellationToken cancellationToken = default);
    Task<System.Drawing.Point?> GetElementClickablePointAsync(TargetSelector target, int timeoutMs = 3000, CancellationToken cancellationToken = default);
}

public sealed class UiaAutomationAdapter : IUiaAutomationAdapter
{
    public async Task<AutomationElement?> FindElementAsync(TargetSelector target, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        if (target is null) return null;

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs && !cancellationToken.IsCancellationRequested)
        {
            var element = FindElementCore(target);
            if (element is not null)
                return element;

            await Task.Delay(100, cancellationToken);
        }

        return null;
    }

    public async Task<bool> VerifyElementAsync(TargetSelector target, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        var element = await FindElementAsync(target, timeoutMs, cancellationToken);
        if (element is null) return false;

        try
        {
            return !element.Current.IsOffscreen;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TryInvokeElementAsync(TargetSelector target, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        var element = await FindElementAsync(target, timeoutMs, cancellationToken);
        if (element is null) return false;

        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) && pattern is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                return true;
            }
        }
        catch
        {
            // Fall through to false if invocation pattern fails
        }

        return false;
    }

    public async Task<bool> TrySetElementValueAsync(TargetSelector target, string value, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        var element = await FindElementAsync(target, timeoutMs, cancellationToken);
        if (element is null) return false;

        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
            {
                valuePattern.SetValue(value);
                return true;
            }

            // Fallback: set focus to element
            element.SetFocus();
            return false; // Indicating caller should type text
        }
        catch
        {
            return false;
        }
    }

    public async Task<System.Drawing.Point?> GetElementClickablePointAsync(TargetSelector target, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        var element = await FindElementAsync(target, timeoutMs, cancellationToken);
        if (element is null) return null;

        try
        {
            var pt = element.GetClickablePoint();
            return new System.Drawing.Point((int)pt.X, (int)pt.Y);
        }
        catch
        {
            try
            {
                var rect = element.Current.BoundingRectangle;
                if (!rect.IsEmpty)
                {
                    return new System.Drawing.Point(
                        (int)(rect.Left + rect.Width / 2),
                        (int)(rect.Top + rect.Height / 2));
                }
            }
            catch { }
        }

        return null;
    }

    private static AutomationElement? FindElementCore(TargetSelector target)
    {
        AutomationElement root = AutomationElement.RootElement;

        // Scope to specific window if specified
        if (!string.IsNullOrWhiteSpace(target.WindowTitle) || !string.IsNullOrWhiteSpace(target.ProcessName))
        {
            var windowCondition = BuildWindowCondition(target);
            if (windowCondition is not null)
            {
                var window = root.FindFirst(TreeScope.Children, windowCondition);
                if (window is not null)
                    root = window;
            }
        }

        var conditions = new List<Condition>();

        if (!string.IsNullOrWhiteSpace(target.AutomationId))
        {
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, target.AutomationId));
        }

        if (!string.IsNullOrWhiteSpace(target.Name))
        {
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, target.Name));
        }

        if (!string.IsNullOrWhiteSpace(target.ClassName))
        {
            conditions.Add(new PropertyCondition(AutomationElement.ClassNameProperty, target.ClassName));
        }

        if (!string.IsNullOrWhiteSpace(target.ControlType))
        {
            var ct = MapControlType(target.ControlType);
            if (ct is not null)
                conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, ct));
        }

        if (conditions.Count == 0)
        {
            // If only window was specified, the window itself is the element
            return root != AutomationElement.RootElement ? root : null;
        }

        Condition searchCondition = conditions.Count == 1
            ? conditions[0]
            : new AndCondition(conditions.ToArray());

        return root.FindFirst(TreeScope.Descendants, searchCondition);
    }

    private static Condition? BuildWindowCondition(TargetSelector target)
    {
        var conditions = new List<Condition>();

        if (!string.IsNullOrWhiteSpace(target.WindowTitle))
        {
            // Check exact or contains
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, target.WindowTitle));
        }

        if (!string.IsNullOrWhiteSpace(target.ProcessName))
        {
            var procName = System.IO.Path.GetFileNameWithoutExtension(target.ProcessName);
            var procs = Process.GetProcessesByName(procName);
            if (procs.Length > 0)
            {
                conditions.Add(new PropertyCondition(AutomationElement.ProcessIdProperty, procs[0].Id));
            }
        }

        if (conditions.Count == 0) return null;
        return conditions.Count == 1 ? conditions[0] : new AndCondition(conditions.ToArray());
    }

    private static ControlType? MapControlType(string controlType)
    {
        return controlType.ToLowerInvariant() switch
        {
            "button" => ControlType.Button,
            "edit" or "textbox" => ControlType.Edit,
            "checkbox" => ControlType.CheckBox,
            "combobox" => ControlType.ComboBox,
            "list" => ControlType.List,
            "listitem" => ControlType.ListItem,
            "menu" => ControlType.Menu,
            "menuitem" => ControlType.MenuItem,
            "pane" => ControlType.Pane,
            "window" => ControlType.Window,
            "tab" => ControlType.Tab,
            "tabitem" => ControlType.TabItem,
            "text" or "label" => ControlType.Text,
            "hyperlink" => ControlType.Hyperlink,
            "radiobutton" => ControlType.RadioButton,
            _ => null
        };
    }
}
