# Repliq — Secure Desktop Workflow Automation

Repliq is a local-first Windows desktop application that helps users automate repetitive data-entry and workflow tasks with safety, least privilege, and resource consciousness at its core.

## Features

- **Local-First & Offline**: Core recording and playback require no account, no cloud telemetry, and no network connection.
- **Strict Declarative Workflows**: Workflows are stored as versioned JSON documents (Schema Version 1). Arbitrary shell commands, Python, PowerShell, and dynamic script execution are completely forbidden.
- **Defense-in-Depth Policy Engine**: Centralized evaluation with default-deny policies, dangerous executable denylists (`cmd.exe`, `powershell.exe`, etc.), protected window detection (UAC, Windows Security, Repliq's own windows), and confirmation gates for high-impact actions.
- **Safe Execution & Shared Emergency Stop**: Centralized execution controller with timeout bounds, non-idempotent retry prevention, and immediate input cleanup (releasing held mouse buttons and keys on cancellation or failure).
- **Windows DPAPI Secret Vault**: Credentials and tokens are protected using Windows Data Protection API (`ProtectedData`, `DataProtectionScope.CurrentUser`) and are strictly forbidden from appearing in workflow JSON or logs.
- **Sanitized Logging & Audit History**: Automatic redaction of passwords, tokens, API keys, and sensitive form payloads in local SQLite run records.
- **Repliq Quick Panel**: Compact floating window sharing the exact same execution controller and security policies, operable while the main window is minimized or hidden.
- **Optional Gemini AI Workflow Assistant**: Implements modern `gemini-3.8-flash` via HTTP to analyze workflow safety or synthesize workflows from natural language prompts, with strict fail-closed validation on all outputs.

---

## Project Structure

```text
Repliq/
├── AGENTS.md                   # Durable development and safety rules
├── README.md                   # Project overview and guide
├── Repliq.md                   # Product and technical specifications
├── RepliqToDo.md               # Implementation task checklist
├── Repliq.sln                  # Master .NET Solution
├── src/
│   ├── Repliq.Core/            # Models, WorkflowValidator, PolicyEngine, Controllers, Diagnostics
│   ├── Repliq.Windows/         # Win32 SendInput adapter, Global Hotkey Manager
│   ├── Repliq.Storage/         # JSON WorkflowRepository, SQLite History, DPAPI SecretStore, Settings
│   ├── Repliq.Integrations/    # Spreadsheet service, Gemini AI assistant
│   └── Repliq.App/             # WinForms GUI, Semantic Design Tokens, Views, QuickPanelForm
└── tests/
    ├── Repliq.Core.Tests/       # Unit tests for validation, policies, controllers, redaction (32 tests)
    ├── Repliq.Security.Tests/   # Security attack vector and DPAPI tests (22 tests)
    └── Repliq.Integration.Tests/# End-to-end recording-to-playback and UI smoke tests (5 tests)
```

---

## Building and Testing

Prerequisites: Windows 10/11 with .NET 8 SDK installed.

```powershell
# Restore and build the solution
dotnet build Repliq.sln

# Run all automated tests
dotnet test Repliq.sln
```

All 59 automated tests execute against mock automation backends to ensure zero unintended desktop input during testing.

---

## Running the Application

```powershell
dotnet run --project src/Repliq.App/Repliq.App.csproj
```

---

## Security Boundary Disclosure

Repliq validates declarative workflow files, restricts simulated actions, protects stored secrets with DPAPI, and prevents accidental misuse. However:
- Repliq runs as the standard user (`asInvoker`).
- It cannot protect against a hostile actor who already controls the same Windows user account or operating system.
- Stored workflows and SQLite logs reside in the current user's `%LOCALAPPDATA%\Repliq` directory.
