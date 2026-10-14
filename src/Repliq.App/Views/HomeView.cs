using System.Drawing;
using Repliq.Core.Models;
using Repliq.Storage.History;
using Repliq.Storage.Workflows;

namespace Repliq.App.Views;

public sealed class HomeView : UserControl
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IHistoryRepository _historyRepo;
    private readonly Action<string> _navigateAction;
    private readonly Action<string, WorkflowDefinition>? _runWorkflowAction;

    private readonly Label _lblGreeting = new();
    private readonly Label _lblSavedCount = new();
    private readonly Label _lblSuccessRunsCount = new();
    private readonly Panel _pnlWorkflowsList = new();

    public HomeView(
        IWorkflowRepository workflowRepo,
        IHistoryRepository historyRepo,
        Action<string> navigateAction,
        Action<string, WorkflowDefinition>? runWorkflowAction = null)
    {
        _workflowRepo = workflowRepo;
        _historyRepo = historyRepo;
        _navigateAction = navigateAction;
        _runWorkflowAction = runWorkflowAction;

        InitializeComponents();
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(28, 20, 28, 20)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Greeting header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Protection active banner
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Stat cards (Saved & Successful)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 3: "Your workflows" header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4: Workflow cards container
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 5: "+ Create workflow" button

        // 0. Greeting Header
        var pnlHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 18)
        };

        int hour = DateTime.Now.Hour;
        string greeting = hour < 12 ? "Good morning" : (hour < 18 ? "Good afternoon" : "Good evening");
        _lblGreeting.Text = greeting;
        _lblGreeting.Font = UITheme.FontDisplay;
        _lblGreeting.ForeColor = UITheme.Current.PrimaryText;
        _lblGreeting.AutoSize = true;

        var lblSubtitle = new Label
        {
            Text = "Automate repetitive work. Keep control.",
            Font = UITheme.FontBody,
            ForeColor = UITheme.Current.SecondaryText,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        pnlHeader.Controls.AddRange(new Control[] { _lblGreeting, lblSubtitle });
        mainLayout.Controls.Add(pnlHeader, 0, 0);

        // 1. Protection Active Banner (Green Tinted Card)
        var pnlBanner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            BackColor = UITheme.Current.SuccessSurface,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 0, 18)
        };
        pnlBanner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pnlBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlBanner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblBannerIcon = new Label
        {
            Text = "✓",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = UITheme.Current.SuccessText,
            Size = new Size(26, 26),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 2, 8, 0)
        };
        pnlBanner.Controls.Add(lblBannerIcon, 0, 0);

        var pnlBannerText = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Margin = new Padding(0)
        };
        var lblBannerTitle = new Label
        {
            Text = "Protection active",
            Font = UITheme.FontBodyBold,
            ForeColor = UITheme.Current.PrimaryText,
            AutoSize = true
        };
        var lblBannerSub = new Label
        {
            Text = "Local workflows · Policy checks enabled",
            Font = UITheme.FontSmall,
            ForeColor = UITheme.Current.SuccessText,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0)
        };
        pnlBannerText.Controls.AddRange(new Control[] { lblBannerTitle, lblBannerSub });
        pnlBanner.Controls.Add(pnlBannerText, 1, 0);

        var lblRightCheck = new Label
        {
            Text = "✓",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = UITheme.Current.SuccessText,
            Size = new Size(24, 24),
            TextAlign = ContentAlignment.MiddleCenter
        };
        pnlBanner.Controls.Add(lblRightCheck, 2, 0);
        mainLayout.Controls.Add(pnlBanner, 0, 1);

        // 2. Metric Stat Cards (Saved & Successful)
        var pnlStats = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Height = 110,
            Margin = new Padding(0, 0, 0, 22)
        };
        pnlStats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pnlStats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        // Left Stat Card: Saved workflows
        var pnlStatLeft = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.Current.CardSurface,
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        pnlStatLeft.Click += (_, _) => _navigateAction("Workflows");

        var flowLeft = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16, 14, 16, 14)
        };
        flowLeft.Click += (_, _) => _navigateAction("Workflows");

        var lblLeftIcon = new Label
        {
            Text = "🔗",
            Font = new Font("Segoe UI", 12F, FontStyle.Regular),
            ForeColor = UITheme.Current.Accent,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        _lblSavedCount.Text = "00";
        _lblSavedCount.Font = UITheme.FontStatNumber;
        _lblSavedCount.ForeColor = UITheme.Current.PrimaryText;
        _lblSavedCount.AutoSize = true;
        _lblSavedCount.Margin = new Padding(0, 0, 0, 2);

        var lblSavedDesc = new Label
        {
            Text = "Saved workflows",
            Font = UITheme.FontSmall,
            ForeColor = UITheme.Current.SecondaryText,
            AutoSize = true
        };
        flowLeft.Controls.AddRange(new Control[] { lblLeftIcon, _lblSavedCount, lblSavedDesc });
        pnlStatLeft.Controls.Add(flowLeft);
        pnlStats.Controls.Add(pnlStatLeft, 0, 0);

        // Right Stat Card: Successful runs
        var pnlStatRight = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.Current.CardSurface,
            Margin = new Padding(8, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        pnlStatRight.Click += (_, _) => _navigateAction("History");

        var flowRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16, 14, 16, 14)
        };
        flowRight.Click += (_, _) => _navigateAction("History");

        var lblRightIcon = new Label
        {
            Text = "✓",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = UITheme.Current.SuccessText,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        _lblSuccessRunsCount.Text = "00";
        _lblSuccessRunsCount.Font = UITheme.FontStatNumber;
        _lblSuccessRunsCount.ForeColor = UITheme.Current.PrimaryText;
        _lblSuccessRunsCount.AutoSize = true;
        _lblSuccessRunsCount.Margin = new Padding(0, 0, 0, 2);

        var lblSuccessDesc = new Label
        {
            Text = "Successful runs",
            Font = UITheme.FontSmall,
            ForeColor = UITheme.Current.SecondaryText,
            AutoSize = true
        };
        flowRight.Controls.AddRange(new Control[] { lblRightIcon, _lblSuccessRunsCount, lblSuccessDesc });
        pnlStatRight.Controls.Add(flowRight);
        pnlStats.Controls.Add(pnlStatRight, 1, 0);

        mainLayout.Controls.Add(pnlStats, 0, 2);

        // 3. "Your workflows" Section Header
        var pnlWfHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        pnlWfHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlWfHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblSectionTitle = new Label
        {
            Text = "Your workflows",
            Font = UITheme.FontHeader,
            ForeColor = UITheme.Current.PrimaryText,
            AutoSize = true,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pnlWfHeader.Controls.Add(lblSectionTitle, 0, 0);

        var btnViewAll = new ThemedButton
        {
            Text = "View all →",
            Tag = "subtle",
            Font = UITheme.FontBody,
            Size = new Size(95, 30),
            Cursor = Cursors.Hand
        };
        btnViewAll.Click += (_, _) => _navigateAction("Workflows");
        pnlWfHeader.Controls.Add(btnViewAll, 1, 0);

        mainLayout.Controls.Add(pnlWfHeader, 0, 3);

        // 4. Workflow Cards Container
        _pnlWorkflowsList.Dock = DockStyle.Fill;
        _pnlWorkflowsList.AutoSize = true;
        _pnlWorkflowsList.Margin = new Padding(0, 0, 0, 16);
        mainLayout.Controls.Add(_pnlWorkflowsList, 0, 4);

        // 5. "+ Create workflow" Prominent Button
        var btnCreateWf = new ThemedButton
        {
            Text = "+ Create workflow",
            Tag = "primary",
            Dock = DockStyle.Fill,
            Height = 44,
            Font = UITheme.FontBodyBold,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 12)
        };
        btnCreateWf.Click += (_, _) => _navigateAction("Editor");
        mainLayout.Controls.Add(btnCreateWf, 0, 5);

        Controls.Add(mainLayout);
        UITheme.Apply(this);
    }

    public async Task RefreshDataAsync()
    {
        try
        {
            var workflows = await _workflowRepo.GetAllAsync();
            _lblSavedCount.Text = workflows.Count.ToString("D2");

            var runs = await _historyRepo.GetRecentRunsAsync(100);
            int successRuns = runs.Count(r => r.FinalState == ExecutionState.Completed);
            _lblSuccessRunsCount.Text = successRuns.ToString("D2");

            RenderWorkflowCards(workflows.Take(4).ToList());
        }
        catch { }
    }

    private void RenderWorkflowCards(List<WorkflowDefinition> workflows)
    {
        _pnlWorkflowsList.Controls.Clear();
        _pnlWorkflowsList.SuspendLayout();

        if (workflows.Count == 0)
        {
            var pnlEmpty = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = UITheme.Current.CardSurface,
                Padding = new Padding(16),
                Margin = new Padding(0, 0, 0, 8)
            };
            var lblEmpty = new Label
            {
                Text = "No saved workflows yet. Click '+ Create workflow' or Record to get started.",
                Font = UITheme.FontBody,
                ForeColor = UITheme.Current.SecondaryText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlEmpty.Controls.Add(lblEmpty);
            _pnlWorkflowsList.Controls.Add(pnlEmpty);
            _pnlWorkflowsList.ResumeLayout();
            return;
        }

        // Add cards in reverse order so Dock = Top stacks them properly
        for (int i = workflows.Count - 1; i >= 0; i--)
        {
            var wf = workflows[i];
            var card = CreateWorkflowCard(wf);
            _pnlWorkflowsList.Controls.Add(card);
        }

        _pnlWorkflowsList.ResumeLayout();
    }

    private Control CreateWorkflowCard(WorkflowDefinition wf)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 62,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = UITheme.Current.CardSurface,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 8),
            Cursor = Cursors.Hand
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Icon Box
        var pnlIconBox = new Panel
        {
            Size = new Size(38, 38),
            BackColor = UITheme.Current.WindowBackground,
            Margin = new Padding(0, 2, 12, 0)
        };
        var lblIcon = new Label
        {
            Text = "📋",
            Font = new Font("Segoe UI", 11F, FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        pnlIconBox.Controls.Add(lblIcon);
        card.Controls.Add(pnlIconBox, 0, 0);

        // Titles
        var pnlInfo = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Margin = new Padding(0)
        };
        var lblTitle = new Label
        {
            Text = wf.Name,
            Font = UITheme.FontBodyBold,
            ForeColor = UITheme.Current.PrimaryText,
            AutoSize = true
        };

        string subtitle = wf.Description ?? (wf.TargetPolicy?.AllowedProcesses.Count > 0
            ? string.Join(", ", wf.TargetPolicy.AllowedProcesses)
            : $"{wf.Actions.Count} actions");

        var lblSub = new Label
        {
            Text = subtitle,
            Font = UITheme.FontSmall,
            ForeColor = UITheme.Current.SecondaryText,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0)
        };
        pnlInfo.Controls.AddRange(new Control[] { lblTitle, lblSub });
        card.Controls.Add(pnlInfo, 1, 0);

        // Play Button
        var btnPlay = new ThemedButton
        {
            Text = "▶",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Size = new Size(34, 34),
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 3, 0, 0)
        };
        btnPlay.Click += (_, _) =>
        {
            if (_runWorkflowAction is not null)
            {
                _runWorkflowAction("Run", wf);
            }
            else
            {
                _navigateAction("Workflows");
            }
        };
        card.Controls.Add(btnPlay, 2, 0);

        // Card click navigates to details or runs
        card.Click += (_, _) =>
        {
            if (_runWorkflowAction is not null)
                _runWorkflowAction("Run", wf);
            else
                _navigateAction("Workflows");
        };

        return card;
    }
}
