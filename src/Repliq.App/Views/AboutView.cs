using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Repliq.App;

namespace Repliq.App.Views;

public sealed class AboutView : UserControl
{
    private readonly Action? _openQuickPanelAction;

    public AboutView(Action? openQuickPanelAction = null)
    {
        _openQuickPanelAction = openQuickPanelAction;
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(28, 20, 28, 20);
        AutoScroll = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoScroll = true
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Hero header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Information cards
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Bottom action bar

        // 1. Hero Header Panel
        var pnlHero = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20)
        };
        pnlHero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        pnlHero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var pbLogo = new PictureBox
        {
            Size = new Size(96, 96),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 12, 0)
        };
        if (UITheme.AppLogo != null)
        {
            pbLogo.Image = UITheme.AppLogo;
        }
        pnlHero.Controls.Add(pbLogo, 0, 0);

        var pnlHeroText = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0)
        };

        var pnlTitleRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 2)
        };

        var lblTitle = new Label
        {
            Text = "Repliq",
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = UITheme.Current.PrimaryText,
            AutoSize = true,
            Margin = new Padding(0, 0, 12, 0)
        };

        var lblVersionBadge = new Label
        {
            Text = "v0.1.0-alpha",
            Font = UITheme.FontSmallBold,
            BackColor = ColorTranslator.FromHtml("#1D4ED8"),
            ForeColor = Color.White,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0, 10, 0, 0)
        };
        pnlTitleRow.Controls.AddRange(new Control[] { lblTitle, lblVersionBadge });

        var lblTagline = new Label
        {
            Text = "Desktop Workflow Automation & Safe Replay Engine",
            Font = UITheme.FontSubheader,
            ForeColor = UITheme.Current.SecondaryText,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };

        var lblAttribution = new Label
        {
            Text = "Developed by RetsardCo (2026)",
            Font = UITheme.FontBodyBold,
            ForeColor = ColorTranslator.FromHtml("#38BDF8"), // Bright sky-blue for prominent visibility
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        pnlHeroText.Controls.AddRange(new Control[] { pnlTitleRow, lblTagline, lblAttribution });
        pnlHero.Controls.Add(pnlHeroText, 1, 0);
        mainLayout.Controls.Add(pnlHero, 0, 0);

        // 2. Information Cards Panel (2 Columns)
        var pnlCards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoScroll = true,
            Margin = new Padding(0, 0, 0, 16)
        };
        pnlCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pnlCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        // Left Card: Architecture & Principles
        var pnlLeftCard = CreateCardPanel("System Architecture & Safety Principles");
        var txtOverview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = UITheme.Current.CardSurface,
            ForeColor = UITheme.Current.PrimaryText,
            Font = UITheme.FontBody,
            Text = 
                "• Developed by RetsardCo 2026\r\n\r\n" +
                "• Dual-Edition Architecture:\r\n" +
                "  1. Repliq Studio (Repliq.App): Full workflow workspace, recorder review, visual editor, and policy manager.\r\n" +
                "  2. Repliq Quick (Repliq.Quick): Zero-bloat standalone floating toolbar for instant recording and hotkey playback.\r\n\r\n" +
                "• Security & Isolation:\r\n" +
                "  Strict declarative JSON workflows (Schema V1). Completely blocks arbitrary shell, PowerShell, or Python script injection.\r\n\r\n" +
                "• Least Privilege & Data Privacy:\r\n" +
                "  100% offline, local-first execution. Zero telemetry. Sensitive credentials encrypted with Windows DPAPI.\r\n\r\n" +
                "• Real-Time Guardrails:\r\n" +
                "  Global emergency stop (Ctrl+Shift+S), protected window detection (UAC/Defender), and strict timeout bounds."
        };
        pnlLeftCard.Controls.Add(txtOverview);
        pnlCards.Controls.Add(pnlLeftCard, 0, 0);

        // Right Card: Environment & Diagnostics
        var pnlRightCard = CreateCardPanel("Environment & Diagnostics");
        var txtEnv = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = UITheme.Current.CardSurface,
            ForeColor = UITheme.Current.PrimaryText,
            Font = UITheme.FontMonospace,
            Text = GetDiagnosticsText()
        };
        pnlRightCard.Controls.Add(txtEnv);
        pnlCards.Controls.Add(pnlRightCard, 1, 0);

        mainLayout.Controls.Add(pnlCards, 0, 1);

        // 3. Bottom Action Buttons
        var pnlActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0)
        };

        var btnCopyDiag = new ThemedButton
        {
            Text = "📋 Copy Diagnostics",
            Size = new Size(160, 36),
            Margin = new Padding(0, 0, 10, 0)
        };
        btnCopyDiag.Click += (_, _) =>
        {
            Clipboard.SetText(GetDiagnosticsText());
            MessageBox.Show("System diagnostics copied to clipboard.", "Diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var btnGitHub = new ThemedButton
        {
            Text = "🌐 GitHub Repository",
            Tag = "primary",
            Size = new Size(170, 36),
            Margin = new Padding(0, 0, 10, 0)
        };
        btnGitHub.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/RetsardCo/Repliq") { UseShellExecute = true });
            }
            catch { }
        };

        pnlActions.Controls.AddRange(new Control[] { btnCopyDiag, btnGitHub });

        if (_openQuickPanelAction != null)
        {
            var btnLaunchQuick = new ThemedButton
            {
                Text = "⚡ Launch Quick Panel",
                Size = new Size(170, 36),
                Margin = new Padding(0, 0, 10, 0)
            };
            btnLaunchQuick.Click += (_, _) => _openQuickPanelAction();
            pnlActions.Controls.Add(btnLaunchQuick);
        }

        mainLayout.Controls.Add(pnlActions, 0, 2);

        Controls.Add(mainLayout);
    }

    private static Panel CreateCardPanel(string headerText)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.Current.CardSurface,
            Padding = new Padding(16),
            Margin = new Padding(4),
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblHeader = new Label
        {
            Text = headerText,
            Font = UITheme.FontSubheader,
            ForeColor = UITheme.Current.PrimaryText,
            Dock = DockStyle.Top,
            Height = 28,
            AutoSize = false
        };
        panel.Controls.Add(lblHeader);
        return panel;
    }

    private static string GetDiagnosticsText()
    {
        var asm = Assembly.GetExecutingAssembly().GetName();
        var localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Repliq");
        return
            $"Application:        Repliq Desktop Automation\r\n" +
            $"Version:            0.1.0-alpha\r\n" +
            $"Developer:          RetsardCo\r\n" +
            $"Copyright:          Copyright © 2026 RetsardCo\r\n" +
            $"Assembly Version:   {asm.Version}\r\n" +
            $"Runtime:            .NET {Environment.Version} (x64)\r\n" +
            $"Operating System:   {Environment.OSVersion}\r\n" +
            $"DPI Awareness:      PerMonitorV2\r\n" +
            $"Data Directory:     {localDir}\r\n" +
            $"Machine Name:       {Environment.MachineName}\r\n" +
            $"User Domain:        {Environment.UserDomainName}\\{Environment.UserName}";
    }
}
