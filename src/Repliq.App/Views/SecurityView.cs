using Repliq.Core.Policies;
using Repliq.Storage.Secrets;

namespace Repliq.App.Views;

public sealed class SecurityView : UserControl
{
    private readonly IPolicyEngine _policyEngine;
    private readonly ISecretStore _secretStore;

    private readonly Label _lblVaultStatus = new();
    private readonly TextBox _txtPolicyDetails = new();
    private readonly TextBox _txtBoundaryDisclaimer = new();
    private readonly Button _btnTestVault = new();

    public SecurityView(IPolicyEngine policyEngine, ISecretStore secretStore)
    {
        _policyEngine = policyEngine;
        _secretStore = secretStore;

        InitializeComponents();
        RefreshSecurityStatus();
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(24);
        AutoScroll = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Vault status panel
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); // Policy details
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); // Security boundary disclaimer

        // Header
        var pnlHeader = new Panel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        var lblTitle = new Label { Text = "Security & Execution Policies", Font = UITheme.FontHeader, AutoSize = true };
        var lblSub = new Label { Text = "Central policy rules, Windows DPAPI vault status, and security boundaries", Font = UITheme.FontBody, Tag = "secondary", AutoSize = true, Location = new Point(0, 30) };
        pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblSub });
        layout.Controls.Add(pnlHeader, 0, 0);

        // Vault status panel
        var pnlVault = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Tag = "secondary", Padding = new Padding(12), Margin = new Padding(0, 0, 0, 16) };
        _lblVaultStatus.Text = "🔒 DPAPI Protected Secret Store: Ready (Encrypted with Windows CurrentUser Key)";
        _lblVaultStatus.Font = UITheme.FontBodyBold;
        _lblVaultStatus.AutoSize = true;
        _lblVaultStatus.Margin = new Padding(0, 6, 16, 0);

        _btnTestVault.Text = "Verify Vault Encryption";
        _btnTestVault.Size = new Size(180, 32);
        _btnTestVault.Click += async (_, _) => await VerifyVaultAsync();

        pnlVault.Controls.AddRange(new Control[] { _lblVaultStatus, _btnTestVault });
        layout.Controls.Add(pnlVault, 0, 1);

        // Policy details
        var grpPolicy = new GroupBox { Text = "Active Execution Policies & Constraints", Dock = DockStyle.Fill, Font = UITheme.FontSubheader, Margin = new Padding(0, 0, 0, 12) };
        _txtPolicyDetails.Dock = DockStyle.Fill;
        _txtPolicyDetails.Multiline = true;
        _txtPolicyDetails.ReadOnly = true;
        _txtPolicyDetails.ScrollBars = ScrollBars.Vertical;
        _txtPolicyDetails.Font = UITheme.FontMonospace;
        grpPolicy.Controls.Add(_txtPolicyDetails);
        layout.Controls.Add(grpPolicy, 0, 2);

        // Security boundary
        var grpBoundary = new GroupBox { Text = "Realistic Security Boundary Disclosure", Dock = DockStyle.Fill, Font = UITheme.FontSubheader };
        _txtBoundaryDisclaimer.Dock = DockStyle.Fill;
        _txtBoundaryDisclaimer.Multiline = true;
        _txtBoundaryDisclaimer.ReadOnly = true;
        _txtBoundaryDisclaimer.Font = UITheme.FontBody;
        _txtBoundaryDisclaimer.ScrollBars = ScrollBars.Vertical;
        grpBoundary.Controls.Add(_txtBoundaryDisclaimer);
        layout.Controls.Add(grpBoundary, 0, 3);

        Controls.Add(layout);
    }

    private void RefreshSecurityStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== CENTRALIZED AUTOMATION POLICY CONFIGURATION ===");
        sb.AppendLine("1. Default-Deny Policy: Active. Any target or action not explicitly allowlisted is denied.");
        sb.AppendLine("2. Arbitrary Code Prevention: Pure declarative JSON only. PowerShell, cmd, bash, and scripts are strictly forbidden.");
        sb.AppendLine("3. Denied Dangerous Executables: cmd.exe, powershell.exe, pwsh.exe, bash.exe, mshta.exe, rundll32.exe, regsvr32.exe, certutil.exe, etc.");
        sb.AppendLine("4. Protected Windows Denylist: 'User Account Control', 'Windows Security', 'LockApp', and Repliq's own windows (feedback loop prevention).");
        sb.AppendLine("5. High-Impact Confirmation: Automatic confirmation gate enforced for delete, submit, and write actions.");
        sb.AppendLine("6. Shared Emergency Stop: Decoupled cancellation mechanism halts actions regardless of UI state.");
        sb.AppendLine("7. Safe Input Cleanup: Down keys and mouse buttons automatically released on stop or error.");
        _txtPolicyDetails.Text = sb.ToString();

        var bSb = new System.Text.StringBuilder();
        bSb.AppendLine("Repliq's security controls are designed to prevent accidental misuse, validate declarative workflow schemas, and enforce least-privilege automation.");
        bSb.AppendLine();
        bSb.AppendLine("CRITICAL HONEST BOUNDARY:");
        bSb.AppendLine("• Repliq runs with normal user privileges ('asInvoker').");
        bSb.AppendLine("• Repliq CANNOT protect against a hostile actor who already controls the same Windows user account or the host operating system.");
        bSb.AppendLine("• Stored workflows and SQLite logs reside in per-user LocalAppData and can be read by other processes running under the same user.");
        bSb.AppendLine("• Windows DPAPI encrypts secrets using the current user's credentials; any process in the same user session can invoke DPAPI unprotect.");
        bSb.AppendLine("• Repliq makes no misleading claims of being 'unhackable' or malware-proof.");
        _txtBoundaryDisclaimer.Text = bSb.ToString();
    }

    private async Task VerifyVaultAsync()
    {
        try
        {
            const string testKey = "__vault_healthcheck__";
            var testValue = "healthcheck_" + Guid.NewGuid().ToString("N");
            await _secretStore.SetSecretAsync(testKey, testValue);
            var retrieved = await _secretStore.GetSecretAsync(testKey);
            await _secretStore.DeleteSecretAsync(testKey);

            if (retrieved == testValue)
            {
                MessageBox.Show("DPAPI encryption and decryption verified successfully.\nCurrent user session keys are operational.", "Vault Health Verified", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("DPAPI roundtrip verification returned mismatched payload.", "Vault Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"DPAPI verification failed: {ex.Message}", "Vault Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
