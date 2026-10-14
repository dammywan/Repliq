using System.Drawing;

namespace Repliq.App;

public sealed class ThemePalette
{
    public Color WindowBackground { get; init; }
    public Color MainSurface { get; init; }
    public Color SecondarySurface { get; init; }
    public Color PrimaryText { get; init; }
    public Color SecondaryText { get; init; }
    public Color Border { get; init; }
    public Color Accent { get; init; }
    public Color Success { get; init; }
    public Color Warning { get; init; }
    public Color Error { get; init; }
    public Color FocusRing { get; init; }
    public bool IsDark { get; init; }
}

public static class UITheme
{
    public static readonly ThemePalette LightPalette = new()
    {
        WindowBackground = ColorTranslator.FromHtml("#F5F6F8"),
        MainSurface = ColorTranslator.FromHtml("#FFFFFF"),
        SecondarySurface = ColorTranslator.FromHtml("#EEF1F5"),
        PrimaryText = ColorTranslator.FromHtml("#1C2430"),
        SecondaryText = ColorTranslator.FromHtml("#586474"),
        Border = ColorTranslator.FromHtml("#D8DEE7"),
        Accent = ColorTranslator.FromHtml("#315FCB"),
        Success = ColorTranslator.FromHtml("#216E45"),
        Warning = ColorTranslator.FromHtml("#8A5700"),
        Error = ColorTranslator.FromHtml("#B42318"),
        FocusRing = ColorTranslator.FromHtml("#245DDA"),
        IsDark = false
    };

    public static readonly ThemePalette DarkPalette = new()
    {
        WindowBackground = ColorTranslator.FromHtml("#14171C"),
        MainSurface = ColorTranslator.FromHtml("#1B2027"),
        SecondarySurface = ColorTranslator.FromHtml("#242B35"),
        PrimaryText = ColorTranslator.FromHtml("#F2F4F7"),
        SecondaryText = ColorTranslator.FromHtml("#B1BAC7"),
        Border = ColorTranslator.FromHtml("#39424F"),
        Accent = ColorTranslator.FromHtml("#AFC6FF"),
        Success = ColorTranslator.FromHtml("#8CD9AC"),
        Warning = ColorTranslator.FromHtml("#F2C66D"),
        Error = ColorTranslator.FromHtml("#FF9B96"),
        FocusRing = ColorTranslator.FromHtml("#AFC6FF"),
        IsDark = true
    };

    public static ThemePalette Current { get; private set; } = LightPalette;

    public static event Action? ThemeChanged;

    public static void SetTheme(string themeName)
    {
        if (string.Equals(themeName, "Dark", StringComparison.OrdinalIgnoreCase))
            Current = DarkPalette;
        else
            Current = LightPalette;

        ThemeChanged?.Invoke();
    }

    public static Font FontHeader => new("Segoe UI", 14F, FontStyle.Bold);
    public static Font FontSubheader => new("Segoe UI", 11F, FontStyle.Bold);
    public static Font FontBody => new("Segoe UI", 9.5F, FontStyle.Regular);
    public static Font FontBodyBold => new("Segoe UI", 9.5F, FontStyle.Bold);
    public static Font FontSmall => new("Segoe UI", 8.5F, FontStyle.Regular);
    public static Font FontMonospace => new("Cascadia Code", 9F, FontStyle.Regular);

    public static void Apply(Control root)
    {
        var p = Current;
        root.BackColor = p.WindowBackground;
        root.ForeColor = p.PrimaryText;
        root.Font = FontBody;

        ApplyRecursive(root, p);
    }

    private static void ApplyRecursive(Control parent, ThemePalette p)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is Button btn)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.BorderColor = p.Border;
                if (btn.Tag as string == "primary")
                {
                    btn.BackColor = p.Accent;
                    btn.ForeColor = p.IsDark ? Color.Black : Color.White;
                    btn.Font = FontBodyBold;
                }
                else if (btn.Tag as string == "danger")
                {
                    btn.BackColor = p.Error;
                    btn.ForeColor = Color.White;
                    btn.Font = FontBodyBold;
                }
                else
                {
                    btn.BackColor = p.MainSurface;
                    btn.ForeColor = p.PrimaryText;
                }
            }
            else if (c is Panel pnl)
            {
                if (pnl.Tag as string == "surface")
                    pnl.BackColor = p.MainSurface;
                else if (pnl.Tag as string == "secondary")
                    pnl.BackColor = p.SecondarySurface;
                else
                    pnl.BackColor = p.WindowBackground;
            }
            else if (c is TextBox or ComboBox or ListBox or ListView or DataGridView)
            {
                c.BackColor = p.MainSurface;
                c.ForeColor = p.PrimaryText;
            }
            else if (c is Label lbl)
            {
                if (lbl.Tag as string == "secondary")
                    lbl.ForeColor = p.SecondaryText;
                else if (lbl.Tag as string == "success")
                    lbl.ForeColor = p.Success;
                else if (lbl.Tag as string == "warning")
                    lbl.ForeColor = p.Warning;
                else if (lbl.Tag as string == "error")
                    lbl.ForeColor = p.Error;
                else
                    lbl.ForeColor = p.PrimaryText;
            }

            if (c.HasChildren)
            {
                ApplyRecursive(c, p);
            }
        }
    }
}
