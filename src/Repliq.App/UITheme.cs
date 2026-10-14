using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Repliq.App;

public sealed class ThemePalette
{
    public Color WindowBackground { get; init; }
    public Color MainSurface { get; init; }
    public Color SecondarySurface { get; init; }
    public Color CardSurface { get; init; }
    public Color CardBorder { get; init; }
    public Color PrimaryText { get; init; }
    public Color SecondaryText { get; init; }
    public Color Border { get; init; }
    public Color Accent { get; init; }
    public Color AccentLight { get; init; }
    public Color AccentLightText { get; init; }
    public Color Success { get; init; }
    public Color SuccessSurface { get; init; }
    public Color SuccessBorder { get; init; }
    public Color SuccessText { get; init; }
    public Color Warning { get; init; }
    public Color WarningSurface { get; init; }
    public Color Error { get; init; }
    public Color ErrorSurface { get; init; }
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
        CardSurface = ColorTranslator.FromHtml("#FFFFFF"),
        CardBorder = ColorTranslator.FromHtml("#D8DEE7"),
        PrimaryText = ColorTranslator.FromHtml("#1C2430"),
        SecondaryText = ColorTranslator.FromHtml("#586474"),
        Border = ColorTranslator.FromHtml("#D8DEE7"),
        Accent = ColorTranslator.FromHtml("#315FCB"),
        AccentLight = ColorTranslator.FromHtml("#E0E7FF"),
        AccentLightText = ColorTranslator.FromHtml("#1E1B4B"),
        Success = ColorTranslator.FromHtml("#10B981"),
        SuccessSurface = ColorTranslator.FromHtml("#ECFDF5"),
        SuccessBorder = ColorTranslator.FromHtml("#A7F3D0"),
        SuccessText = ColorTranslator.FromHtml("#065F46"),
        Warning = ColorTranslator.FromHtml("#F59E0B"),
        WarningSurface = ColorTranslator.FromHtml("#FFFBEB"),
        Error = ColorTranslator.FromHtml("#EF4444"),
        ErrorSurface = ColorTranslator.FromHtml("#FEF2F2"),
        FocusRing = ColorTranslator.FromHtml("#245DDA"),
        IsDark = false
    };

    public static readonly ThemePalette DarkPalette = new()
    {
        WindowBackground = ColorTranslator.FromHtml("#0B0F19"),
        MainSurface = ColorTranslator.FromHtml("#0F172A"),
        SecondarySurface = ColorTranslator.FromHtml("#1E293B"),
        CardSurface = ColorTranslator.FromHtml("#1E293B"),
        CardBorder = ColorTranslator.FromHtml("#334155"),
        PrimaryText = ColorTranslator.FromHtml("#F8FAFC"),
        SecondaryText = ColorTranslator.FromHtml("#94A3B8"),
        Border = ColorTranslator.FromHtml("#334155"),
        Accent = ColorTranslator.FromHtml("#3B82F6"),
        AccentLight = ColorTranslator.FromHtml("#DBEAFE"),
        AccentLightText = ColorTranslator.FromHtml("#0F172A"),
        Success = ColorTranslator.FromHtml("#10B981"),
        SuccessSurface = ColorTranslator.FromHtml("#062A1C"),
        SuccessBorder = ColorTranslator.FromHtml("#059669"),
        SuccessText = ColorTranslator.FromHtml("#34D399"),
        Warning = ColorTranslator.FromHtml("#F59E0B"),
        WarningSurface = ColorTranslator.FromHtml("#2E1F06"),
        Error = ColorTranslator.FromHtml("#EF4444"),
        ErrorSurface = ColorTranslator.FromHtml("#3B1111"),
        FocusRing = ColorTranslator.FromHtml("#60A5FA"),
        IsDark = true
    };

    public static ThemePalette Current { get; private set; } = DarkPalette;

    public static event Action? ThemeChanged;

    static UITheme()
    {
        try { ToolStripManager.Renderer = new DarkToolStripRenderer(); } catch { }
    }

    public static void SetTheme(string themeName)
    {
        if (string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase))
            Current = LightPalette;
        else
            Current = DarkPalette;

        try { ToolStripManager.Renderer = new DarkToolStripRenderer(); } catch { }
        ThemeChanged?.Invoke();
    }

    public static Font FontDisplay => new("Segoe UI", 20F, FontStyle.Bold);
    public static Font FontStatNumber => new("Segoe UI", 24F, FontStyle.Bold);
    public static Font FontHeader => new("Segoe UI", 14F, FontStyle.Bold);
    public static Font FontSubheader => new("Segoe UI", 11F, FontStyle.Bold);
    public static Font FontBody => new("Segoe UI", 9.5F, FontStyle.Regular);
    public static Font FontBodyBold => new("Segoe UI", 9.5F, FontStyle.Bold);
    public static Font FontSmall => new("Segoe UI", 8.5F, FontStyle.Regular);
    public static Font FontSmallBold => new("Segoe UI", 8.5F, FontStyle.Bold);
    public static Font FontMicro => new("Segoe UI", 7.5F, FontStyle.Bold);
    public static Font FontMonospace => new("Cascadia Code", 9F, FontStyle.Regular);

    private static Image? _appLogo;
    public static Image? AppLogo
    {
        get
        {
            if (_appLogo != null) return _appLogo;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("Repliq.App.Resources.logo.png")
                                ?? asm.GetManifestResourceStream("Repliq.Quick.Resources.logo.png");
                if (stream != null)
                {
                    _appLogo = Image.FromStream(stream);
                    return _appLogo;
                }
            }
            catch { }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(baseDir, "Resources", "logo.png"),
                    Path.Combine(baseDir, "assets", "logo.png"),
                    Path.Combine(baseDir, "..", "..", "..", "assets", "logo.png"),
                    Path.Combine(baseDir, "..", "..", "..", "src", "Repliq.App", "Resources", "logo.png")
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        _appLogo = Image.FromFile(c);
                        return _appLogo;
                    }
                }
            }
            catch { }
            return null;
        }
    }

    private static Icon? _appIcon;
    public static Icon? AppIcon
    {
        get
        {
            if (_appIcon != null) return _appIcon;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("Repliq.App.Resources.app.ico")
                                ?? asm.GetManifestResourceStream("Repliq.Quick.Resources.app.ico");
                if (stream != null)
                {
                    _appIcon = new Icon(stream);
                    return _appIcon;
                }
            }
            catch { }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(baseDir, "Resources", "app.ico"),
                    Path.Combine(baseDir, "assets", "logo.ico"),
                    Path.Combine(baseDir, "..", "..", "..", "assets", "logo.ico"),
                    Path.Combine(baseDir, "..", "..", "..", "src", "Repliq.App", "Resources", "app.ico")
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        _appIcon = new Icon(c);
                        return _appIcon;
                    }
                }
            }
            catch { }
            return null;
        }
    }

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
            if (c is ThemedButton or QuickToolbarButton)
            {
                continue;
            }

            if (c is Button btn)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.BorderColor = p.Border;
                btn.Paint -= OnStandardButtonPaint;
                btn.Paint += OnStandardButtonPaint;
                var tag = btn.Tag as string;
                if (tag == "primary")
                {
                    btn.BackColor = p.Accent;
                    btn.ForeColor = Color.White;
                    btn.FlatAppearance.BorderColor = p.Accent;
                    btn.Font = FontBodyBold;
                }
                else if (tag == "accent")
                {
                    btn.BackColor = p.Accent;
                    btn.ForeColor = Color.White;
                    btn.FlatAppearance.BorderColor = p.Accent;
                    btn.Font = FontBodyBold;
                }
                else if (tag == "danger")
                {
                    btn.BackColor = p.Error;
                    btn.ForeColor = Color.White;
                    btn.FlatAppearance.BorderColor = p.Error;
                    btn.Font = FontBodyBold;
                }
                else if (tag == "card")
                {
                    btn.BackColor = p.CardSurface;
                    btn.ForeColor = p.PrimaryText;
                    btn.FlatAppearance.BorderColor = p.CardBorder;
                }
                else if (tag == "subtle")
                {
                    btn.BackColor = p.SecondarySurface;
                    btn.ForeColor = p.SecondaryText;
                    btn.FlatAppearance.BorderColor = p.CardBorder;
                }
                else
                {
                    btn.BackColor = p.SecondarySurface;
                    btn.ForeColor = p.PrimaryText;
                }
            }
            else if (c is Panel pnl)
            {
                var tag = pnl.Tag as string;
                if (tag == "surface")
                    pnl.BackColor = p.MainSurface;
                else if (tag == "secondary" || tag == "card")
                    pnl.BackColor = p.SecondarySurface;
                else if (tag == "success_card")
                    pnl.BackColor = p.SuccessSurface;
                else if (tag == "window")
                    pnl.BackColor = p.WindowBackground;
                else if (tag == "transparent")
                    pnl.BackColor = Color.Transparent;
            }
            else if (c is ComboBox cmb)
            {
                StyleComboBox(cmb, p);
            }
            else if (c is NumericUpDown nud)
            {
                nud.BackColor = p.SecondarySurface;
                nud.ForeColor = p.PrimaryText;
                nud.BorderStyle = BorderStyle.FixedSingle;
                foreach (Control child in nud.Controls)
                {
                    child.BackColor = p.SecondarySurface;
                    child.ForeColor = p.PrimaryText;
                }
            }
            else if (c is TextBox tb)
            {
                tb.BackColor = p.SecondarySurface;
                tb.ForeColor = p.PrimaryText;
                tb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is ListBox lb)
            {
                lb.BackColor = p.SecondarySurface;
                lb.ForeColor = p.PrimaryText;
                lb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is CheckBox cb)
            {
                cb.ForeColor = p.PrimaryText;
            }
            else if (c is RadioButton rb)
            {
                rb.ForeColor = p.PrimaryText;
            }
            else if (c is GroupBox gb)
            {
                gb.ForeColor = p.PrimaryText;
            }
            else if (c is LinkLabel ll)
            {
                ll.LinkColor = p.Accent;
                ll.ActiveLinkColor = p.AccentLight;
                ll.VisitedLinkColor = p.Accent;
            }
            else if (c is ListView lv)
            {
                StyleListView(lv, p);
            }
            else if (c is DataGridView dgv)
            {
                dgv.BackgroundColor = p.SecondarySurface;
                dgv.DefaultCellStyle.BackColor = p.SecondarySurface;
                dgv.DefaultCellStyle.ForeColor = p.PrimaryText;
            }
            else if (c is Label lbl)
            {
                var tag = lbl.Tag as string;
                if (tag == "secondary")
                    lbl.ForeColor = p.SecondaryText;
                else if (tag == "success")
                    lbl.ForeColor = p.SuccessText;
                else if (tag == "warning")
                    lbl.ForeColor = p.Warning;
                else if (tag == "error")
                    lbl.ForeColor = p.Error;
                else if (tag == "accent")
                    lbl.ForeColor = p.Accent;
                else if (tag == "accent_light")
                    lbl.ForeColor = p.AccentLight;
                else
                    lbl.ForeColor = p.PrimaryText;
            }

            if (c.HasChildren)
            {
                ApplyRecursive(c, p);
            }
        }
    }

    private static void OnStandardButtonPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not Button btn) return;
        if (!btn.Enabled)
        {
            var p = Current;
            var g = e.Graphics;
            var rect = btn.ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            Color bg = p.IsDark ? ColorTranslator.FromHtml("#162032") : ColorTranslator.FromHtml("#E2E8F0");
            Color border = p.IsDark ? ColorTranslator.FromHtml("#2D3D52") : ColorTranslator.FromHtml("#CBD5E1");
            Color fg = ColorTranslator.FromHtml("#94A3B8");

            using (var brush = new SolidBrush(bg))
            {
                g.FillRectangle(brush, rect);
            }

            using (var pen = new Pen(border, 1f))
            {
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, btn.Text, btn.Font, rect, fg, flags);
        }
    }

    public static void StyleListView(ListView lv, ThemePalette p)
    {
        lv.BackColor = p.SecondarySurface;
        lv.ForeColor = p.PrimaryText;
        lv.BorderStyle = BorderStyle.FixedSingle;
        lv.OwnerDraw = true;

        lv.DrawColumnHeader -= OnListViewDrawColumnHeader;
        lv.DrawItem -= OnListViewDrawItem;
        lv.DrawSubItem -= OnListViewDrawSubItem;

        lv.DrawColumnHeader += OnListViewDrawColumnHeader;
        lv.DrawItem += OnListViewDrawItem;
        lv.DrawSubItem += OnListViewDrawSubItem;
    }

    private static void OnListViewDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var p = Current;
        var g = e.Graphics;
        var rect = e.Bounds;

        using (var brush = new SolidBrush(p.SecondarySurface))
        {
            g.FillRectangle(brush, rect);
        }

        using (var pen = new Pen(p.CardBorder, 1))
        {
            g.DrawLine(pen, rect.Left, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
            g.DrawLine(pen, rect.Right - 1, rect.Top + 3, rect.Right - 1, rect.Bottom - 4);
        }

        var textRect = new Rectangle(rect.X + 8, rect.Y, Math.Max(0, rect.Width - 12), rect.Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, e.Header?.Text ?? string.Empty, FontBodyBold, textRect, p.PrimaryText, flags);
    }

    private static void OnListViewDrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        // Drawing handled per-subitem in DrawSubItem
    }

    private static void OnListViewDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var p = Current;
        var g = e.Graphics;
        var rect = e.Bounds;

        bool isSelected = e.Item != null && e.Item.Selected;

        Color bg;
        Color fg;

        if (isSelected)
        {
            bg = ColorTranslator.FromHtml("#1D4ED8");
            fg = Color.White;
        }
        else
        {
            bg = (e.ItemIndex % 2 == 0) ? p.CardSurface : p.SecondarySurface;
            fg = (e.Item != null && e.Item.ForeColor != Color.Empty && e.Item.ForeColor != SystemColors.WindowText)
                ? (e.Item.ForeColor == Color.Gray ? ColorTranslator.FromHtml("#94A3B8") : e.Item.ForeColor)
                : p.PrimaryText;
        }

        using (var brush = new SolidBrush(bg))
        {
            g.FillRectangle(brush, rect);
        }

        var textRect = new Rectangle(rect.X + 8, rect.Y, Math.Max(0, rect.Width - 12), rect.Height);
        var font = (e.Item != null && e.Item.Font != null) ? e.Item.Font : FontBody;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        TextRenderer.DrawText(g, e.SubItem?.Text ?? string.Empty, font, textRect, fg, flags);
    }

    public static void StyleComboBox(ComboBox cmb, ThemePalette p)
    {
        cmb.BackColor = p.SecondarySurface;
        cmb.ForeColor = p.PrimaryText;
        if (cmb.DropDownStyle == ComboBoxStyle.DropDownList)
        {
            cmb.DrawMode = DrawMode.OwnerDrawFixed;
            cmb.ItemHeight = Math.Max(24, cmb.Font.Height + 6);
            cmb.DrawItem -= OnComboBoxDrawItem;
            cmb.DrawItem += OnComboBoxDrawItem;
        }
    }

    private static void OnComboBoxDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || sender is not ComboBox cmb) return;

        var p = Current;
        var g = e.Graphics;
        bool isSelected = (e.State & DrawItemState.Selected) != 0;

        Color bg = isSelected ? ColorTranslator.FromHtml("#1D4ED8") : p.SecondarySurface;
        Color fg = isSelected ? Color.White : p.PrimaryText;

        using (var brush = new SolidBrush(bg))
        {
            g.FillRectangle(brush, e.Bounds);
        }

        string text = cmb.Items[e.Index]?.ToString() ?? string.Empty;
        var textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 8), e.Bounds.Height);
        TextRenderer.DrawText(g, text, cmb.Font ?? FontBody, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    public static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        if (d > rect.Width) d = rect.Width;
        if (d > rect.Height) d = rect.Height;
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public class ThemedButton : Button
{
    private bool _isHovered;
    private bool _isPressed;

    public ThemedButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = UITheme.FontBodyBold;
        Margin = new Padding(2);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var p = UITheme.Current;
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        Color bg;
        Color border;
        Color fg;

        string? tag = Tag as string;

        if (!Enabled)
        {
            bg = p.IsDark ? ColorTranslator.FromHtml("#162032") : ColorTranslator.FromHtml("#E2E8F0");
            border = p.IsDark ? ColorTranslator.FromHtml("#2D3D52") : ColorTranslator.FromHtml("#CBD5E1");
            fg = ColorTranslator.FromHtml("#94A3B8");
        }
        else if (tag == "primary")
        {
            bg = _isPressed ? ColorTranslator.FromHtml("#1D4ED8") : (_isHovered ? ColorTranslator.FromHtml("#3B82F6") : ColorTranslator.FromHtml("#2563EB"));
            border = _isHovered ? ColorTranslator.FromHtml("#60A5FA") : bg;
            fg = Color.White;
        }
        else if (tag == "danger")
        {
            bg = _isPressed ? ColorTranslator.FromHtml("#991B1B") : (_isHovered ? ColorTranslator.FromHtml("#DC2626") : ColorTranslator.FromHtml("#EF4444"));
            border = _isHovered ? ColorTranslator.FromHtml("#F87171") : bg;
            fg = Color.White;
        }
        else if (tag == "accent")
        {
            bg = _isPressed ? ColorTranslator.FromHtml("#1D4ED8") : (_isHovered ? ColorTranslator.FromHtml("#3B82F6") : p.Accent);
            border = _isHovered ? ColorTranslator.FromHtml("#60A5FA") : bg;
            fg = Color.White;
        }
        else
        {
            bg = _isPressed ? (p.IsDark ? ColorTranslator.FromHtml("#0F172A") : ColorTranslator.FromHtml("#E2E8F0"))
                            : (_isHovered ? (p.IsDark ? ColorTranslator.FromHtml("#334155") : ColorTranslator.FromHtml("#F1F5F9")) : p.CardSurface);
            border = _isHovered ? p.Accent : p.CardBorder;
            fg = p.PrimaryText;
        }

        using (var brush = new SolidBrush(bg))
        {
            g.FillRectangle(brush, rect);
        }

        using (var pen = new Pen(border, 1f))
        {
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, Text, Font, rect, fg, flags);
    }
}

public enum ToolbarGlyph
{
    None,
    Pause,
    Resume,
    Stop,
    Options,
    Pin,
    Close
}

public sealed class QuickToolbarButton : Button
{
    private bool _isHovered;
    private bool _isPressed;
    private bool _isRecordingActive;
    private bool _isPlayButton;
    private bool _isPinned = true;
    private ToolbarGlyph _glyph = ToolbarGlyph.None;
    private Color? _activeBorder;

    public ToolbarGlyph Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value;
            Invalidate();
        }
    }

    public bool IsRecordingActive
    {
        get => _isRecordingActive;
        set
        {
            _isRecordingActive = value;
            Invalidate();
        }
    }

    public bool IsPlayButton
    {
        get => _isPlayButton;
        set
        {
            _isPlayButton = value;
            Invalidate();
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            _isPinned = value;
            Invalidate();
        }
    }

    public Color? ActiveBorder
    {
        get => _activeBorder;
        set
        {
            _activeBorder = value;
            Invalidate();
        }
    }

    public QuickToolbarButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        Margin = new Padding(2);
        Dock = DockStyle.Fill;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    public Color GetCurrentForeColor()
    {
        if (!Enabled)
        {
            // Clear muted silver for disabled state — high contrast on #1E293B
            return ColorTranslator.FromHtml("#94A3B8");
        }

        if (_isRecordingActive || _isPlayButton || (_glyph == ToolbarGlyph.Pin && _isPinned))
        {
            return Color.White;
        }

        if (_glyph == ToolbarGlyph.Close)
        {
            return _isHovered ? Color.White : ColorTranslator.FromHtml("#CBD5E1");
        }

        if (_glyph == ToolbarGlyph.Stop)
        {
            return _isHovered ? Color.White : ColorTranslator.FromHtml("#EF4444");
        }

        if (_glyph == ToolbarGlyph.Resume)
        {
            return ColorTranslator.FromHtml("#F59E0B");
        }

        if (_glyph == ToolbarGlyph.Pin && !_isPinned)
        {
            return _isHovered ? Color.White : ColorTranslator.FromHtml("#94A3B8");
        }

        if (_glyph == ToolbarGlyph.Options)
        {
            return _isHovered ? Color.White : ColorTranslator.FromHtml("#F8FAFC");
        }

        return _isHovered ? Color.White : ColorTranslator.FromHtml("#F8FAFC");
    }

    public Color GetCurrentBackColor()
    {
        if (!Enabled)
        {
            return UITheme.Current.CardSurface;
        }

        if (_isRecordingActive)
        {
            return _isPressed
                ? ColorTranslator.FromHtml("#DC2626")
                : (_isHovered ? ColorTranslator.FromHtml("#F87171") : UITheme.Current.Error);
        }

        if (_isPlayButton)
        {
            return _isPressed
                ? ColorTranslator.FromHtml("#2563EB")
                : (_isHovered ? ColorTranslator.FromHtml("#60A5FA") : UITheme.Current.Accent);
        }

        if (_glyph == ToolbarGlyph.Pin && _isPinned)
        {
            return _isPressed
                ? ColorTranslator.FromHtml("#2563EB")
                : (_isHovered ? ColorTranslator.FromHtml("#60A5FA") : UITheme.Current.Accent);
        }

        if (_glyph == ToolbarGlyph.Close && _isHovered)
        {
            return ColorTranslator.FromHtml("#EF4444");
        }

        if (_glyph == ToolbarGlyph.Stop && _isHovered)
        {
            return ColorTranslator.FromHtml("#EF4444");
        }

        return _isPressed
            ? ColorTranslator.FromHtml("#0F172A")
            : (_isHovered ? ColorTranslator.FromHtml("#334155") : UITheme.Current.CardSurface);
    }

    public Color GetCurrentBorderColor()
    {
        if (!Enabled)
        {
            return UITheme.Current.CardBorder;
        }

        if (_isRecordingActive)
        {
            return ColorTranslator.FromHtml("#DC2626");
        }

        if (_isPlayButton)
        {
            return ColorTranslator.FromHtml("#2563EB");
        }

        if (_glyph == ToolbarGlyph.Pin && _isPinned)
        {
            return ColorTranslator.FromHtml("#2563EB");
        }

        if (_glyph == ToolbarGlyph.Close && _isHovered)
        {
            return ColorTranslator.FromHtml("#DC2626");
        }

        if (_activeBorder.HasValue)
        {
            return _activeBorder.Value;
        }

        return _isHovered ? ColorTranslator.FromHtml("#475569") : UITheme.Current.CardBorder;
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        var g = pe.Graphics;
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        Color bg = GetCurrentBackColor();
        Color border = GetCurrentBorderColor();
        Color fg = GetCurrentForeColor();

        // 1. Draw Background
        using (var bgBrush = new SolidBrush(bg))
        {
            g.FillRectangle(bgBrush, 0, 0, rect.Width, rect.Height);
        }

        // 2. Draw 1px Border
        using (var borderPen = new Pen(border, 1))
        {
            g.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);
        }

        // 3. Draw Glyphs or Text
        int cx = rect.Width / 2;
        int cy = rect.Height / 2;

        if (_glyph == ToolbarGlyph.Pause)
        {
            int barW = 3;
            int barH = 12;
            int gap = 3;
            int totalW = barW * 2 + gap;
            int startX = cx - totalW / 2;
            int startY = cy - barH / 2;

            using var brush = new SolidBrush(fg);
            g.FillRectangle(brush, startX, startY, barW, barH);
            g.FillRectangle(brush, startX + barW + gap, startY, barW, barH);
        }
        else if (_glyph == ToolbarGlyph.Resume)
        {
            int size = 10;
            Point[] tri =
            {
                new Point(cx - size / 2, cy - size / 2),
                new Point(cx - size / 2, cy + size / 2),
                new Point(cx + size / 2 + 1, cy)
            };
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(fg);
            g.FillPolygon(brush, tri);
            g.SmoothingMode = SmoothingMode.Default;
        }
        else if (_glyph == ToolbarGlyph.Stop)
        {
            int size = 10;
            int x = cx - size / 2;
            int y = cy - size / 2;
            using var brush = new SolidBrush(fg);
            g.FillRectangle(brush, x, y, size, size);
        }
        else if (_glyph == ToolbarGlyph.Options)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var toothPen = new Pen(fg, 2f);
            using var ringPen = new Pen(fg, 1.8f);

            int outerR = 6;
            int innerR = 4;
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4.0;
                float x1 = cx + (float)(Math.Cos(angle) * innerR);
                float y1 = cy + (float)(Math.Sin(angle) * innerR);
                float x2 = cx + (float)(Math.Cos(angle) * outerR);
                float y2 = cy + (float)(Math.Sin(angle) * outerR);
                g.DrawLine(toothPen, x1, y1, x2, y2);
            }

            g.DrawEllipse(ringPen, cx - 4.5f, cy - 4.5f, 9, 9);
            using var bgBrush = new SolidBrush(bg);
            g.FillEllipse(bgBrush, cx - 1.5f, cy - 1.5f, 3, 3);
            g.SmoothingMode = SmoothingMode.Default;
        }
        else if (_glyph == ToolbarGlyph.Pin)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(fg);
            using var pen = new Pen(fg, 1.5f);

            if (_isPinned)
            {
                // Pinned down: solid pin pointing straight down
                g.FillEllipse(brush, cx - 4, cy - 6, 8, 4);
                g.FillRectangle(brush, cx - 2, cy - 3, 4, 5);
                g.DrawLine(pen, cx, cy + 2, cx, cy + 7);
            }
            else
            {
                // Unpinned: tilted pin
                g.DrawEllipse(pen, cx - 4, cy - 6, 8, 4);
                g.DrawRectangle(pen, cx - 2, cy - 3, 4, 5);
                g.DrawLine(pen, cx, cy + 2, cx + 3, cy + 7);
            }
            g.SmoothingMode = SmoothingMode.Default;
        }
        else if (_glyph == ToolbarGlyph.Close)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(fg, 1.8f);
            int s = 4;
            g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
            g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
            g.SmoothingMode = SmoothingMode.Default;
        }
        else if (!string.IsNullOrEmpty(Text))
        {
            if (Text == "● Rec")
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int dotSize = 7;
                var textSize = TextRenderer.MeasureText(g, "Rec", Font, rect.Size, TextFormatFlags.NoPadding);
                int totalW = dotSize + 5 + textSize.Width;
                int startX = cx - totalW / 2;
                int dotY = cy - dotSize / 2;

                using (var redBrush = new SolidBrush(ColorTranslator.FromHtml("#EF4444")))
                {
                    g.FillEllipse(redBrush, startX, dotY, dotSize, dotSize);
                }
                g.SmoothingMode = SmoothingMode.Default;

                var textRect = new Rectangle(startX + dotSize + 5, cy - textSize.Height / 2, textSize.Width, textSize.Height);
                TextRenderer.DrawText(g, "Rec", Font, textRect, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                TextRenderer.DrawText(
                    g,
                    Text,
                    Font,
                    rect,
                    fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}

public sealed class QuickMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => UITheme.Current.MainSurface;
    public override Color MenuBorder => UITheme.Current.CardBorder;
    public override Color MenuItemBorder => UITheme.Current.CardBorder;
    public override Color MenuItemSelected => ColorTranslator.FromHtml("#334155");
    public override Color MenuStripGradientBegin => UITheme.Current.MainSurface;
    public override Color MenuStripGradientEnd => UITheme.Current.MainSurface;
    public override Color MenuItemSelectedGradientBegin => ColorTranslator.FromHtml("#334155");
    public override Color MenuItemSelectedGradientEnd => ColorTranslator.FromHtml("#334155");
    public override Color MenuItemPressedGradientBegin => ColorTranslator.FromHtml("#1E293B");
    public override Color MenuItemPressedGradientEnd => ColorTranslator.FromHtml("#1E293B");
    public override Color ImageMarginGradientBegin => UITheme.Current.MainSurface;
    public override Color ImageMarginGradientMiddle => UITheme.Current.MainSurface;
    public override Color ImageMarginGradientEnd => UITheme.Current.MainSurface;
    public override Color SeparatorDark => UITheme.Current.CardBorder;
    public override Color SeparatorLight => UITheme.Current.CardBorder;
    public override Color CheckBackground => ColorTranslator.FromHtml("#1E293B");
    public override Color CheckSelectedBackground => ColorTranslator.FromHtml("#334155");
    public override Color CheckPressedBackground => ColorTranslator.FromHtml("#1E293B");
}

public sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    public DarkToolStripRenderer() : base(new QuickMenuColorTable())
    {
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(UITheme.Current.MainSurface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(UITheme.Current.MainSurface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(UITheme.Current.CardBorder, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled)
        {
            return;
        }

        if (e.Item.Selected || (e.Item is ToolStripMenuItem mi && mi.DropDown.Visible))
        {
            var bounds = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using var bgBrush = new SolidBrush(ColorTranslator.FromHtml("#334155"));
            e.Graphics.FillRectangle(bgBrush, bounds);

            using var borderPen = new Pen(ColorTranslator.FromHtml("#475569"), 1);
            e.Graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }
        else
        {
            using var bgBrush = new SolidBrush(UITheme.Current.MainSurface);
            e.Graphics.FillRectangle(bgBrush, new Rectangle(Point.Empty, e.Item.Size));
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        Color color = GetItemTextColor(e.Item);
        // Do NOT delegate to base.OnRenderItemText(e) because WinForms' base implementation
        // ignores e.TextColor when e.Item.Enabled is false and invokes ControlPaint.DrawStringDisabled,
        // which produces illegible near-black text on dark backgrounds.
        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            e.TextRectangle,
            color,
            e.TextFormat);
    }

    public static Color GetItemTextColor(ToolStripItem item)
    {
        if (!item.Enabled)
        {
            return ColorTranslator.FromHtml("#64748B");
        }
        if (item.Selected)
        {
            return Color.White;
        }
        return ColorTranslator.FromHtml("#F8FAFC");
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        var checkRect = new Rectangle(r.X + 2, r.Y + 2, Math.Max(14, r.Width - 4), Math.Max(14, r.Height - 4));

        using var boxBrush = new SolidBrush(ColorTranslator.FromHtml("#1E293B"));
        using var boxPen = new Pen(ColorTranslator.FromHtml("#3B82F6"), 1.5f);
        e.Graphics.FillRectangle(boxBrush, checkRect);
        e.Graphics.DrawRectangle(boxPen, checkRect);

        using var checkPen = new Pen(Color.White, 2f);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Point[] points =
        {
            new Point(checkRect.Left + 3, checkRect.Top + checkRect.Height / 2),
            new Point(checkRect.Left + checkRect.Width / 2 - 1, checkRect.Bottom - 4),
            new Point(checkRect.Right - 3, checkRect.Top + 4)
        };
        e.Graphics.DrawLines(checkPen, points);
        e.Graphics.SmoothingMode = SmoothingMode.Default;
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(UITheme.Current.CardBorder, 1);
        e.Graphics.DrawLine(pen, 28, y, e.Item.Width - 6, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        bool isEnabled = e.Item?.Enabled ?? true;
        bool isSelected = e.Item?.Selected ?? false;
        e.ArrowColor = !isEnabled
            ? ColorTranslator.FromHtml("#64748B")
            : (isSelected ? Color.White : ColorTranslator.FromHtml("#94A3B8"));
        base.OnRenderArrow(e);
    }
}

