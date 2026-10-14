# Repliq — Product and Technical Specification

**Document type:** Product requirements, architecture, security specification, and engineering guide  
**Product:** Repliq  
**Developer / Vendor:** RetsardCo (2026)  
**Version:** 0.1.0-alpha  
**License:** MIT License (Copyright © 2026 RetsardCo)  
**Target platform:** Windows 10/11 x64, with Windows 11 as the primary test target  
**Status:** Alpha Release (v0.1.0-alpha)  
**Primary objective:** Secure, reliable, and resource-conscious desktop workflow automation

---

## 1. Product Overview

Repliq is a local-first Windows desktop application that helps users automate repetitive data-entry tasks. Users can record a sequence of permitted actions, inspect and edit the sequence, save it as a reusable workflow, and run it later using the application or a configurable keyboard shortcut.

The product should support a progression from simple desktop macro playback to structured workflows that can interact with supported Windows controls, spreadsheets, and browser forms. The design must not assume that every application exposes accessible controls or that coordinate-based playback will always be reliable.

### 1.1 Product principles

1. **Security before convenience:** workflows are untrusted input until validated; permissions are enforced by the execution engine.
2. **Local-first:** no account, cloud service, telemetry upload, or network connection is required for core recording and playback.
3. **Least privilege:** run as the normal user; never request administrator rights for ordinary automation.
4. **Small and efficient:** avoid large frameworks, always-running background services, bundled browsers, and unnecessary dependencies.
5. **Explicit control:** show when recording or playback is active; provide pause, stop, and emergency cancellation.
6. **Observable behavior:** show the next action, target, result, and reason for failure where practical.
7. **Fail closed:** if validation, target verification, or policy checks fail, stop rather than continuing blindly.
8. **Modular growth:** browser, spreadsheet, database, and scripting capabilities are optional integrations, not prerequisites for the core recorder.
9. **No arbitrary workflow code:** workflows use a constrained declarative action format rather than executable Python, PowerShell, JavaScript, or shell snippets.
10. **Honest security claims:** local controls reduce risk but cannot guarantee safety if Windows, the user's account, or the running process is fully compromised.

### 1.2 Intended users and use cases

- Repeating data entry into internal business applications.
- Copying structured values between spreadsheets and supported forms.
- Filling repetitive web forms where the user is authorized to automate the process.
- Replaying routine desktop interactions that are stable and permitted.
- Creating reusable, auditable workflows for personal or small-team productivity.

### 1.3 Out of scope for the initial release

- Stealth, hidden monitoring, keylogging, credential harvesting, or surveillance of other users.
- Bypassing MFA, CAPTCHA, access controls, rate limits, or application security policies.
- Automatically accepting legal agreements, making payments, deleting records, or submitting irreversible actions without an explicit confirmation policy.
- Running arbitrary scripts or installing user-supplied executable plugins.
- Remote control, cloud orchestration, multi-user administration, or unattended operation on locked desktops.
- Guaranteeing compatibility with every Windows application, website, or database.
- Claiming that local encryption or logs can protect data from an attacker who already controls the same user account or operating system.

---

## 2. Recommended Technology

### 2.1 Primary stack

- **Language/runtime:** C# with a current supported .NET release compatible with the chosen Windows UI framework.
- **Desktop UI:** WinForms for a small, conventional Windows interface; consider WPF only if the UI requirements justify its additional complexity. Make the choice during Phase 1 and keep one framework.
- **Windows interaction:** Microsoft UI Automation (UIA) for supported controls; controlled Win32 input injection only for actions that require it.
- **Storage:** SQLite for metadata and run history, plus versioned JSON workflow files. Avoid storing secrets in workflow JSON.
- **Testing:** xUnit or NUnit, with isolated unit tests and a separate controlled integration-test suite.
- **Packaging:** `dotnet publish` with an explicitly chosen framework-dependent or self-contained deployment profile.
- **Version control:** Git with reviewable changes and a clean working tree before releases.

Use a supported .NET version at implementation time. Do not hard-code an obsolete target framework merely because it appears in an example.

### 2.2 Lightweight deployment strategy

“Ultra-lightweight” is a goal to measure, not a claim to make before profiling.

- Prefer the simplest native Windows UI framework that meets product needs.
- Do not embed Chromium or another browser engine. Use the user's installed browser for optional browser automation.
- Avoid loading automation backends until a workflow needs them.
- Do not run a permanent polling loop when event-driven notifications or a sleeping worker will suffice.
- Avoid large dependency trees, machine-learning models, cloud SDKs, and unnecessary services.
- Store workflows as small declarative documents and use bounded in-memory queues.
- Keep startup work minimal; load history and optional modules on demand.
- Offer framework-dependent distribution for users with the required .NET runtime and self-contained distribution when frictionless installation matters more.
- Test trimming or Native AOT only after confirming all dependencies and UI technologies support it; do not trade away reliability for a theoretical size reduction.
- Measure cold startup, idle CPU, idle and active memory, package size, and playback overhead on a documented test machine.

### 2.3 Why Windows UI Automation

Windows UI Automation exposes properties and control patterns for many desktop UI elements, allowing automation to target controls by accessible identity rather than only by screen coordinates. Coverage depends on the target application's UI Automation provider and control implementation. Repliq must retain a coordinate-based fallback for compatible tasks, with stronger verification and warnings.

Reference: Microsoft, “UI Automation Overview”  
https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview

### 2.4 Deployment trade-offs

.NET supports single-file publishing, but a self-contained package includes runtime components and may be substantially larger than a framework-dependent package. Single-file publishing does not automatically mean the smallest file, fastest startup, or lowest memory usage. Select deployment mode based on measured size and the target users' installation constraints.

Reference: Microsoft, “Create a single file for application deployment”  
https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview

---

## 3. Functional Requirements

### 3.1 Main window and workflow management

The application should provide:
- A clear Record, Stop Recording, Play, Pause, Resume, and Stop Playback interface.
- A visible recording/playback status indicator.
- A workflow list with name, last-run time, and status.
- Create, rename, duplicate, import, export, and delete workflow operations.
- An action timeline showing sequence order, action type, delay, target, and validation state.
- Search/filter for actions and a way to disable or remove an action.
- A settings page for shortcuts, safety policies, storage, and diagnostics.
- A run-history view that avoids exposing sensitive values.
- Keyboard accessibility for core UI functions.

Do not hide active recording or playback in the system tray without a persistent visible indication. A tray icon may be optional, but it must not conceal state.

### 3.2 Recording

The recorder may capture:
- Mouse button and click events.
- Pointer coordinates and display information when coordinate playback is selected.
- Keyboard events required to reproduce ordinary input, subject to sensitive-field exclusions.
- Scroll events.
- Time deltas between actions, with reasonable minimum and maximum bounds.
- Foreground process/window identity where available.
- UI Automation element metadata when it can be captured safely and reliably.

Recording requirements:
- Recording begins only after an explicit user action.
- Show a prominent recording indicator and elapsed time.
- Provide a global stop-recording shortcut.
- Exclude Repliq's own window from capture where practical to avoid feedback loops.
- Avoid collecting input while a configured sensitive application or sensitive field is active.
- Never record password characters, authentication codes, or secret values by default.
- Do not record raw clipboard contents automatically. Clipboard use must be an explicit workflow action with clear disclosure.
- Bound event count, duration, and memory consumption; warn and stop safely at configured limits.
- Store captured events locally and avoid automatic network transmission.
- Allow users to review and redact the recording before saving.

Important limitation: global keyboard hooks can observe sensitive input if implemented carelessly. Sensitive-field detection is imperfect. Repliq should default to conservative exclusions and offer a clear recording indicator rather than claiming it can identify every secret field.

### 3.3 Workflow editing

The editor should allow users to:
- Rename actions and workflows.
- Change bounded delays.
- Delete, duplicate, reorder, or disable actions.
- Add an explicit wait with timeout.
- Add text-entry actions and variables.
- Select a target application or window.
- Choose UI Automation targeting or coordinate targeting when supported.
- Add validation steps and confirmation gates.
- Preview the actions in a readable, non-executable form.
- Validate the entire workflow before it can run.

Do not provide an arbitrary code editor in the initial release.

### 3.4 Playback

Playback must:
- Execute only a workflow that passed schema and policy validation.
- Run through a single execution controller that owns cancellation and policy checks.
- Support Play, Pause, Resume, Stop, and emergency cancellation.
- Check cancellation between actions and during waits.
- Enforce a maximum action count, workflow duration, retry count, and per-action timeout.
- Revalidate target identity immediately before sensitive actions.
- Stop on an unexpected window, missing target, failed verification, or policy violation.
- Avoid blindly retrying non-idempotent actions.
- Report the last successful action and the reason for stopping.
- Release simulated held keys/buttons on cancellation or failure where technically possible.
- Restore input state conservatively; never attempt to compensate with unbounded actions.
- Require explicit user confirmation before configured high-impact actions.
- Never run on the secure desktop, bypass a UAC prompt, or attempt to automate sign-in screens.

Coordinate playback can be affected by display scaling, resolution changes, window movement, application layout changes, focus changes, and unexpected dialogs. It must have configurable target-window checks and a clear warning that coordinates are less robust than accessible control selectors.

### 3.5 Global shortcuts

- Allow the user to configure shortcuts for start/stop recording, play/pause, resume, and emergency stop.
- Detect shortcut conflicts and report registration failures.
- Provide a safe default emergency stop shortcut and allow the user to change it.
- Keep emergency cancellation independent of the normal playback queue.
- Do not allow imported workflows to change global shortcuts.
- Do not register hidden or deceptive shortcuts.
- Test shortcuts when the application is unfocused and document Windows limitations.

### 3.6 Spreadsheet automation

Initial spreadsheet support should focus on file-based `.xlsx` operations:
- Read selected sheets and ranges.
- Map source columns to named workflow variables.
- Validate cell types, required fields, row counts, and destination values.
- Preview a bounded sample before processing the full dataset.
- Keep an original copy or provide an explicit backup option before bulk modification.
- Use a dry-run mode that reports intended changes without writing.
- Record row counts and non-sensitive error summaries.
- Do not silently overwrite files or sheets.
- Use temporary output and atomic replacement where supported, with explicit handling of backup and recovery.

Direct automation of an already-open Excel UI can be a later integration. Do not make Office interop a requirement for the initial recorder.

### 3.7 Browser automation

Browser automation is an optional later module:
- Prefer stable accessible selectors or supported Playwright locators over coordinates.
- Scope browser workflows to configured domains and permitted tabs.
- Display the target domain before execution.
- Stop when navigation unexpectedly leaves the allowlist or the expected page structure changes.
- Avoid storing cookies, browser profiles, session tokens, or passwords in workflow files.
- Use the user's authorized session; do not bypass MFA, CAPTCHA, or access controls.
- Require confirmation for actions that submit, publish, send, purchase, or delete.
- Do not package a browser engine unless a later, justified requirement demands it.

Browser automation can be technically more reliable than coordinate replay for web forms, but website changes and anti-automation controls can still break workflows. Respect website terms and organizational policy.

### 3.8 Database automation

Database support must be deferred until the core engine and security model are stable.
- Use explicitly configured connections and least-privilege credentials.
- Prefer read-only access for initial database workflows.
- Parameterize SQL queries; never concatenate untrusted values into SQL.
- Never execute SQL from imported workflow text without a separately designed, reviewed query feature.
- Require explicit confirmation and, where feasible, transactions for writes.
- Show the target database and intended row count before bulk updates/deletes.
- Provide dry-run/preview where feasible and rollback where supported.
- Never save database passwords in workflow JSON or logs.

---

## 4. Workflow Format and Validation

### 4.1 Declarative workflow model

Use a versioned JSON schema. A workflow should contain only known action types and bounded data, not executable code.

Illustrative shape:

```json
{
  "schemaVersion": 1,
  "id": "generated-unique-id",
  "name": "Example workflow",
  "createdUtc": "2026-01-01T00:00:00Z",
  "targetPolicy": {
    "allowedProcesses": ["example.exe"],
    "allowedDomains": []
  },
  "limits": {
    "maxActions": 500,
    "maxDurationSeconds": 300
  },
  "actions": [
    {
      "type": "wait",
      "durationMs": 250
    },
    {
      "type": "typeText",
      "value": {
        "kind": "variable",
        "name": "customerName"
      }
    }
  ]
}
```

This example is illustrative, not a final schema. The implementation must define every field, allowed enum, length limit, and validation rule. Avoid including actual personal data or secrets in sample workflows.

### 4.2 Validation requirements

On import, save, and immediately before execution:
- Enforce a known schema version.
- Reject unknown action types and unsupported properties.
- Reject malformed JSON, duplicate IDs, invalid references, and invalid enum values.
- Limit file size, action count, string lengths, nesting depth, wait duration, retry count, and total runtime.
- Reject paths and URIs outside configured policies.
- Treat all imported values as untrusted.
- Reject attempts to request arbitrary commands, dynamic code, process injection, or unapproved executable launches.
- Normalize paths before allowlist checks and guard against path traversal.
- Validate the whole workflow before starting; do not execute a valid prefix of an invalid workflow.
- Revalidate policy and target identity during execution.
- Never trust a workflow's own declared permissions as authorization.

### 4.3 Workflow integrity

A checksum can detect accidental corruption but does not authenticate who created a workflow. If trusted workflow signing is introduced later, use an established signing format and protect signing keys. Do not invent a cryptographic scheme. Unsigned imported workflows should be treated as untrusted and shown in a review screen.

---

## 5. Security Model

### 5.1 Assets to protect

- User-entered data and clipboard contents.
- Workflow definitions and their integrity.
- Application configuration and allowlists.
- Credentials, API tokens, database connection secrets, and browser session material.
- Run logs and screenshots, if screenshots are ever introduced.
- The user's active applications and files.
- The integrity of Repliq updates and dependencies.

### 5.2 Threat actors and failure sources

- Malicious or compromised workflow files.
- Accidental user misconfiguration.
- Malicious content in a website, document, spreadsheet, or clipboard.
- A compromised third-party dependency or update.
- A malicious local process running with the same user privileges.
- A remote attacker exploiting a vulnerability in a connected application or browser.
- An attacker with administrator or full user-account control.
- Unexpected application layout changes or transient UI dialogs.

### 5.3 Core controls

**Least privilege**
- Do not run elevated by default.
- Do not install a Windows service for the MVP.
- Use explicit file and application permissions.
- Avoid broad filesystem, process-launch, or network capabilities.

**Execution policy**
- Use a centralized policy engine that checks every action.
- Default to deny unknown actions and targets.
- Restrict allowed processes, windows, URLs, file paths, and operation types.
- Require confirmation for high-impact operations.
- Fail closed when policy state is unavailable or inconsistent.

**Secrets**
- Keep secrets out of JSON workflows, source code, logs, exception messages, and crash reports.
- Use Windows DPAPI or Credential Manager through established APIs.
- Minimize secret lifetime in memory and never promise perfect memory erasure in managed code.
- Provide a way to remove stored credentials.

**Storage**
- Store per-user data in an appropriate per-user application-data directory.
- Use restrictive filesystem permissions where possible.
- Protect configuration from unintended modification.
- Avoid storing sensitive screenshots or raw keystroke histories.
- Consider SQLite integrity checks and safe transaction handling; database encryption is not a substitute for OS account security.

**Logging**
- Log workflow ID, start/end time, action type, outcome, and safe error codes.
- Redact typed values by default; log variable names or redacted placeholders instead of values.
- Do not log passwords, tokens, clipboard contents, full form payloads, or database connection strings.
- Rotate logs and bound storage use.
- Explain that local logs can be altered by an attacker controlling the same account. Tamper-evident hash chains may reveal some modifications but are not proof against a privileged attacker who can rewrite the log and its state.

**Dependencies and updates**
- Pin or centrally manage dependency versions and review dependency changes.
- Use trusted package sources and inspect security advisories.
- Keep a software bill of materials where practical.
- Do not download or execute code during ordinary workflow playback.
- If updates are implemented, verify update signatures and integrity before installation, fail closed on verification errors, and use a secure release process.
- Do not build an auto-updater into the MVP unless it can be designed and tested properly.

**User transparency**
- Show a clear recording indicator.
- Show the target application/window before a run.
- Preview high-impact changes.
- Provide Stop and emergency-stop behavior.
- Explain what is recorded and where it is stored.
- Make data retention and deletion controls visible.

### 5.4 Emergency stop and cancellation

- The execution controller must use a cancellation token or equivalent shared cancellation mechanism.
- Every wait and action must be bounded and cancellation-aware.
- The emergency-stop handler must not wait for the GUI thread or a long-running action to finish before setting cancellation.
- Playback must check cancellation before and after every action.
- If a native operation cannot be interrupted safely, document the limitation and stop subsequent actions immediately.
- On stop/failure, attempt to release simulated mouse buttons and keys and restore Repliq's own state.
- Never claim the emergency stop can interrupt every external application call instantaneously.
- Test stop behavior during long waits, repeated clicks, text entry, target loss, and UI thread stalls.

### 5.5 Realistic security boundary

Repliq can validate workflows, restrict its own actions, protect stored secrets, and reduce accidental misuse. It cannot guarantee protection when a hostile actor controls Windows, has the user's privileges, injects into the process, or changes the application binary/configuration with sufficient rights. Do not advertise Repliq as malware-proof, unhackable, or capable of defending a fully compromised host.

---

## 6. Architecture

Use small modules with explicit interfaces. Avoid a monolithic `MainWindow` or a single class that records, validates, stores, and executes workflows.

### 6.1 Suggested solution layout

```text
Repliq/
├── AGENTS.md
├── README.md
├── Repliq.md
├── RepliqToDo.md
├── Repliq.sln
├── src/
│   ├── Repliq.App/
│   │   ├── Program.cs
│   │   ├── MainForm.cs
│   │   └── Views/
│   ├── Repliq.Core/
│   │   ├── Models/
│   │   ├── Recording/
│   │   ├── Playback/
│   │   ├── Validation/
│   │   └── Policies/
│   ├── Repliq.Windows/
│   │   ├── Input/
│   │   ├── UIAutomation/
│   │   └── Hotkeys/
│   ├── Repliq.Storage/
│   │   ├── Workflows/
│   │   ├── History/
│   │   └── Secrets/
│   └── Repliq.Integrations/
│       ├── Spreadsheets/
│       ├── Browser/
│       └── Database/
└── tests/
    ├── Repliq.Core.Tests/
    ├── Repliq.Security.Tests/
    └── Repliq.Integration.Tests/
```

This is a proposed layout. Antigravity may adjust it if there is a clear reason, but must preserve separation of concerns and explain material changes.

### 6.2 Module responsibilities

- **App/UI:** user interaction and state display; must not contain automation policy logic.
- **Core models:** workflow, action, execution result, policy decision, and error types.
- **Recorder:** converts observed events into sanitized action records.
- **Workflow validator:** parses and validates schema and limits.
- **Policy engine:** approves or denies actions against configured permissions.
- **Execution controller:** serializes execution, handles cancellation, and coordinates targets and policies.
- **Input backend:** narrowly scoped mouse/keyboard operations behind an interface.
- **UI Automation adapter:** discovers and manipulates supported accessible controls.
- **Hotkey service:** registers visible, configurable shortcuts and reports conflicts.
- **Storage:** writes validated workflow files and bounded execution history.
- **Secret store:** retrieves secrets only when required, using Windows-protected storage.
- **Integrations:** optional spreadsheet, browser, and database modules with separate permissions.
- **Diagnostics:** redacted logs and safe error reporting.

### 6.3 Threading and responsiveness

- Keep UI work on the UI thread.
- Use a background worker or task for recording processing and playback orchestration.
- Marshal status updates back to the UI safely.
- Do not block the UI thread on delays, file I/O, browser calls, or UI Automation searches.
- Serialize input execution so concurrent workflows cannot fight over focus or input.
- Bound event queues and cancel cleanly during application shutdown.

### 6.4 Data flow

1. User starts recording.
2. Recorder captures eligible events and applies filtering/redaction.
3. The action timeline displays the sanitized events.
4. User reviews and saves a workflow.
5. Workflow is validated before storage and again before execution.
6. User starts playback.
7. The policy engine authorizes the target and action.
8. The execution controller performs one bounded action at a time.
9. Each outcome is logged using redacted fields.
10. Completion, failure, or cancellation is shown with a safe summary.

---

## 7. Performance and Resource Budgets

These are **initial engineering targets**, not verified results. Revisit them after measuring the first prototype on a documented Windows machine.

- Idle CPU: target effectively 0% average over a representative five-minute idle sample, excluding brief OS/UI activity.
- Idle memory: target below approximately 100 MB private working set for the minimal recorder UI, if the selected framework and measured build make this feasible.
- Startup: target a responsive main window within 2 seconds on the reference machine after warm-up; measure cold and warm startup separately.
- Playback overhead: avoid adding unnecessary polling; waits should use asynchronous/delay mechanisms rather than busy loops.
- Event processing: no unbounded queue growth; test long recordings and enforce limits.
- UI responsiveness: controls remain usable while workflows run and while cancellation is requested.
- Packaging: publish both framework-dependent and self-contained builds for comparison; record exact sizes rather than assuming one is smaller.
- Background operation: no network requests or recurring polling when no recording or workflow is active.

### 7.1 Measurement protocol

Document:
- Windows version/build, CPU, RAM, display scaling, .NET runtime, and build configuration.
- Package size and dependency inventory.
- Cold startup and warm startup across at least 10 runs; report median and range.
- Idle CPU and private working set over five minutes after settling.
- Memory after recording 1,000 events and after releasing the recording.
- Playback CPU and responsiveness for a bounded 500-action test workflow.
- Behavior after repeated start/stop cycles.
- Whether the application returns to its idle baseline after a workflow ends.

Do not optimize prematurely or disable essential validation merely to meet a target. If a target is missed, profile first and document the trade-off.

---

## 8. Testing and Quality Strategy

### 8.1 Unit tests

Test:
- Workflow schema parsing and version handling.
- Rejection of unknown action types and properties.
- Action-count, string-length, timeout, and duration limits.
- Path and URL allowlist normalization.
- Policy decisions for allowed and denied actions.
- Cancellation during waits and between actions.
- Retry limits and prevention of unsafe retries.
- Secret redaction in logs.
- Workflow serialization round-trips.
- Corrupt storage and invalid workflow recovery.

### 8.2 Integration tests

Use a dedicated test application/window and temporary files:
- Record a controlled sequence and confirm expected sanitized actions.
- Replay against a test window without touching real user data.
- Change window focus and verify that playback stops or refuses to proceed.
- Remove or rename a target control and verify fail-closed behavior.
- Test display scaling and resolution changes for coordinate workflows.
- Verify hotkey conflict handling and emergency stop.
- Test app shutdown during a workflow.
- Test file-write failure, low disk space, and corrupt workflow files.

### 8.3 Security tests

- Import malformed, oversized, deeply nested, and adversarial workflow files.
- Attempt unsupported action types, path traversal, URL allowlist bypasses, and unknown target requests.
- Verify that workflow values cannot invoke arbitrary commands or load assemblies.
- Search logs, databases, workflow files, and crash output for seeded test secrets.
- Confirm that policy denial prevents the action from reaching the input backend.
- Test cancellation during long waits and repeated actions.
- Test workflow tampering and version mismatches.
- Run dependency and static-analysis checks as part of release verification.

Security tests must use a controlled environment and test data. Never test destructive operations against production applications or real databases.

### 8.4 Release gates

A release candidate must not ship if:
- Any critical security test fails.
- Invalid workflows can execute.
- Emergency stop fails to prevent subsequent actions in a controlled test.
- Logs contain seeded secrets.
- The application requests elevation without an approved and documented reason.
- A build or dependency is unverified.
- The app silently continues after an unexpected target change.

---

## 9. Development Roadmap

### Phase 1 — Requirements and foundation
Confirm stack, threat model, user stories, action schema, target platforms, and measurable performance baselines.

### Phase 2 — UI shell and HCI foundation
Implement Section 12 design tokens, main navigation, Home, Workflows, and mock recorder/player. Add keyboard/accessibility and display-scaling checks. No real input injection yet.

### Phase 3 — Core models and validation
Implement the declarative workflow format, schema validation, limits, policy decisions, and unit tests.

### Phase 4 — Recording
Implement controlled input capture, visible recording state, redaction, event filtering, and bounded storage.

### Phase 5 — Playback and emergency stop
Implement sequential playback, cancellation, timeout policies, target checks, and safe cleanup.

### Phase 6 — Persistence and history
Save/import/export workflows, protect settings, and maintain redacted execution history.

### Phase 7 — UI Automation targeting
Use accessible control properties/patterns where available; keep coordinate replay as an explicitly less reliable mode.

### Phase 8 — Spreadsheet integration
Add `.xlsx` mapping, preview, dry-run, backups, validation, and safe output.

### Phase 9 — Browser integration
Add domain-restricted, selector-based workflows only after the core security and cancellation model passes tests.

### Phase 10 — Database integration
Begin with read-only connectors; design and test write transactions and confirmation gates separately.

### Phase 11 — Packaging and optimization
Compare deployment options, measure startup/idle footprint, verify the release artifact, and test on a clean Windows profile.

---

## 10. Antigravity Development Rules

Create `AGENTS.md` with these durable rules:

1. Read `Repliq.md` and `RepliqToDo.md` before making changes.
2. Implement one small phase or task at a time.
3. Do not claim a task is complete without running relevant tests.
4. Do not add dependencies without explaining the purpose and footprint.
5. Do not add arbitrary script execution or unreviewed plugin loading.
6. Keep UI, core workflow logic, Windows adapters, storage, and security policy separate.
7. Use mock input backends in unit tests.
8. Do not execute real desktop input during automated unit tests.
9. Do not store or log real credentials, tokens, or personal data.
10. Treat imported workflows, clipboard content, websites, and spreadsheet cells as untrusted.
11. Fail closed when validation or authorization fails.
12. Update the TODO checklist and document material design decisions.
13. Report test commands, results, unresolved issues, and known limitations.
14. Do not promise absolute security or a specific memory/package-size target without measurements.
15. Never silently weaken a security control to make a test pass.

### First implementation task

Create the solution and project skeleton, establish the UI framework, add the workflow model and validation tests, and build a non-operational UI prototype using mock data. Do not implement global hooks or real input injection until the policy and cancellation interfaces are reviewed.

---

## 11. References

- Microsoft Learn — UI Automation Overview: https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview
- Microsoft Learn — UI Automation Fundamentals: https://learn.microsoft.com/en-us/windows/win32/winauto/entry-uiautocore-overview
- Microsoft Learn — Single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- Microsoft Learn — .NET application publishing overview: https://learn.microsoft.com/en-us/dotnet/articles/core/deploying/index

These references explain relevant platform capabilities and deployment trade-offs; they do not certify Repliq's implementation as secure or performant.


---

# 12. HCI, UI, and UX Design Specification

This section is normative product guidance for the Repliq interface. The GUI must make automation understandable and controllable for first-time users without hiding important safety decisions from advanced users.

## 12.1 UX vision and product personality

**Design intent:** a calm, precise, professional Windows productivity application. Repliq should feel dependable rather than futuristic, playful, or intimidating.

- Prioritize clarity, predictable behavior, and visible system state.
- Use a compact native desktop layout instead of a web-dashboard aesthetic with excessive cards.
- Make recording and playback state impossible to miss.
- Explain safety decisions in plain language, including what was blocked and how to resolve it.
- Prefer direct manipulation and familiar Windows conventions.
- Avoid animations, decorative charts, gamification, and persistent notifications that do not help the user complete a task.
- Use progressive disclosure: common actions are immediately visible; advanced configuration appears when editing a workflow or opening its security settings.
- Never represent a workflow as “safe” merely because it passed schema validation. Distinguish validation, permission approval, target verification, and execution outcome.

### Core UX promise

A user should be able to answer these questions at any time:

1. What workflow am I editing or running?
2. Is Repliq recording, playing, paused, or idle?
3. Which application or website will be affected?
4. What is the next action?
5. What has been verified, and what has not?
6. What permissions does this workflow have?
7. How do I pause or stop it?
8. If it fails, what happened and what is safe to do next?

## 12.2 Primary user journeys

### Journey A — Record a simple task

1. User selects **New workflow**.
2. Repliq asks for a workflow name and shows a concise explanation of what will be recorded.
3. User selects **Record**.
4. A persistent recording indicator appears, including a clear Stop control and shortcut.
5. User performs the task in the target application.
6. User stops recording.
7. Repliq opens the Recorder Review screen with a sanitized action list.
8. User removes accidental actions, labels important steps, and saves.
9. Repliq validates the workflow and reports issues without claiming that the workflow is guaranteed to work.

### Journey B — Run an existing workflow

1. User selects a workflow.
2. The workflow summary shows its target application, permission scope, last result, and verification coverage.
3. User selects **Dry run** or **Run**.
4. Repliq validates the workflow, target, and policy.
5. If the workflow includes a configured high-impact action, the user sees a focused confirmation with the target and action described explicitly.
6. The Run Monitor shows progress and verification outcomes.
7. On success, Repliq displays a concise completion summary.
8. On failure, Repliq stops, explains the failed step, and presents safe next actions.

### Journey C — Recover after failure

1. Repliq stops at the failed or ambiguous step.
2. The Run Monitor identifies the last confirmed checkpoint.
3. Repliq distinguishes **known not completed**, **verified completed**, and **outcome unknown**.
4. If the outcome is unknown, Repliq must not offer a one-click blind retry for a potentially non-idempotent action.
5. The user may inspect the target application, mark the situation resolved where appropriate, restart, or resume from a verified checkpoint.
6. The run history retains a redacted summary of the failure and recovery choice.

### Journey D — Review a workflow's permissions

1. User opens a workflow and selects **Security & permissions**.
2. Repliq lists the target applications/domains, allowed operation types, file access scope, and confirmation gates.
3. Each permission explains its practical effect in plain language.
4. Changes are shown as a diff before saving.
5. A permission expansion requires an explicit user action; imported workflow content cannot silently grant itself new permissions.
6. The policy engine revalidates the workflow before the next run.

## 12.3 Information architecture and navigation

Use one main window with a persistent left navigation rail/sidebar and a central content region. Keep the top-level navigation stable across screens.

Recommended navigation:

- **Home** — overview, recent workflows, recent runs, and primary create/record action.
- **Workflows** — searchable workflow library and workflow details.
- **Run history** — previous runs, outcomes, and recovery details.
- **Security** — global policy defaults, protected storage status, and security events.
- **Settings** — shortcuts, appearance, storage, diagnostics, and accessibility options.

Contextual screens opened from a workflow:

- **Recorder** — active capture and post-recording review.
- **Workflow editor** — action sequence, variables, conditions, and verification.
- **Dry run & permissions** — intended changes, policy results, and confirmation gates.
- **Run monitor** — live playback state and step-level results.

Do not add separate top-level navigation entries for every contextual screen. Use a breadcrumb or back button to return to the workflow context. Do not rely on color alone to indicate state.

## 12.4 Main window layout

### Desktop composition

- **Title bar:** application name/icon, current workspace or workflow title when relevant, and standard Windows window controls.
- **Left navigation:** fixed-width sidebar that can collapse to icons only when the window is narrow; collapsed icons require tooltips and accessible names.
- **Main content:** page title, one-line contextual description, primary action, then content.
- **Optional contextual panel:** properties or security details for the selected action; hide it when it would make the main workspace too narrow.
- **Status area:** show relevant local status such as recording/playback state, pending validation, or storage warning. Do not display decorative telemetry.
- **Global emergency stop:** available during recording and playback, with a configurable shortcut. It must remain independent of the normal page layout.

The interface should not require multiple floating windows for normal use. A focused modal dialog is acceptable for critical confirmation, but the user should retain enough context to understand what they are approving.

### Minimum-size behavior

Choose and test a practical minimum window size during implementation; a starting target is approximately 960 × 640 logical pixels, subject to usability testing. Below the supported minimum, prevent the window from becoming unusably narrow or switch to a deliberate compact layout. Do not let the action list and properties panel overlap.

Support Windows display scaling and multiple monitors. Store window placement safely and recover if a saved position is off-screen after a monitor change.

## 12.5 Screen specifications

### A. Home

**Purpose:** give users a clear starting point without becoming an analytics dashboard.

Layout:
1. Page heading: “Home” with a short explanation.
2. Primary action: **Create workflow**.
3. Secondary action: **Record a task**.
4. Recent workflows list, with name, target type, last run, and status.
5. Recent activity list with successful, failed, or cancelled runs.
6. A compact security-status summary based on actual checks.
7. A small local-storage/diagnostic area only when attention is needed.

Each workflow row should provide a single primary open action and a clearly discoverable Run action. Avoid placing destructive actions next to Run without spacing or confirmation.

Empty state:
- Explain what a workflow is in one sentence.
- Provide **Record your first workflow** and **Create a workflow manually**.
- Do not show fabricated statistics or sample records as if they were real.

### B. Workflows library

**Purpose:** find, organize, inspect, and run saved workflows.

Components:
- Search field with a clear button.
- Optional filters: All, Needs review, Ready to run, Recently used.
- Sort options such as name, last run, and last modified.
- Compact list or table with workflow name, target, last outcome, and validation state.
- Per-row overflow menu for Rename, Duplicate, Export, and Delete.
- Details pane or dedicated detail page for permissions, variables, and recent run outcomes.

UX rules:
- “Ready to run” means all required static checks passed; it is not a guarantee of success.
- Show the reason for “Needs review” directly.
- Confirm deletion and explain whether run history is also deleted.
- Imported workflows receive an **Untrusted import — review required** state until validated and reviewed.
- Search should match names and user-authored descriptions, not sensitive recorded values.

### C. Recorder and recording review

**Active recording state:**
- Persistent, high-contrast **Recording** label with a recording icon and elapsed time.
- Prominent Stop Recording button.
- Visible target application/window.
- A short reminder that sensitive input is excluded where detectable and that users should not record passwords or authentication codes.
- No raw stream of typed characters on screen by default.

**Post-recording review:**
- Heading with workflow name and recording duration.
- Ordered action timeline.
- Each row shows action type, target summary, and delay; text payloads are masked or summarized by default.
- Controls to remove, disable, reorder, or rename an action.
- A warning for coordinate-only actions or unresolved targets.
- Save and Validate actions.
- A summary of excluded or redacted events when it can be provided without exposing sensitive content.

Do not auto-save a raw keystroke log in a way that bypasses review and redaction. Avoid capturing clipboard data unless the user explicitly adds a clipboard action.

### D. Workflow editor

Use a three-region layout on sufficiently wide windows:

1. **Action list/timeline:** ordered workflow steps.
2. **Main canvas/details:** selected step description and inline verification/condition information.
3. **Properties panel:** editable parameters, target selection, timeout, and action-specific settings.

On smaller windows, collapse the properties panel into a side pane or dialog; do not shrink all three regions until they are unreadable.

Action row requirements:
- Step number and action icon.
- Human-readable label, not only a technical event name.
- Target summary.
- Verification status.
- Overflow menu for duplicate, disable, delete, and move.
- Clear visual distinction between disabled, invalid, unverified, and currently selected steps.

Editor behavior:
- Undo/redo for editing operations where practical.
- Unsaved-changes indicator and confirmation before discarding changes.
- Validation errors linked to the exact action and property.
- Reordering via keyboard as well as drag-and-drop.
- Do not require drag-and-drop to perform essential tasks.
- Do not display secrets as plain text in the properties panel.
- Imported actions that exceed policy must be visible and blocked, not silently removed.

### E. Dry Run & Permissions

**Purpose:** let users understand intended behavior before execution.

Show:
- Workflow name and target application/domain.
- Validation summary.
- Permission summary: allowed and blocked operations.
- Planned steps with expected targets.
- Data mapping preview where applicable.
- Required confirmation gates.
- Known limitations, such as coordinate-only target matching or missing post-action verification.

For data-entry preview:
- Use a table with source field, destination field, sanitized preview, and validation status.
- Mask sensitive values.
- Provide a sample-row preview and a total-row count.
- Show duplicate warnings only when supported by an explicit, documented comparison rule.
- For bulk changes, show the affected range/record count and backup/dry-run state.

Primary actions:
- **Back to edit**
- **Run dry run**
- **Run workflow** only when policy permits it
- **Review permissions** when blocked

Confirmation design:
- State the exact action and target in plain language.
- Explain why confirmation is required.
- Use explicit verbs such as “Submit 12 records” rather than vague labels such as “Continue”.
- Do not preselect the destructive/high-impact option.
- Require a deliberate click or keyboard action.
- Never use a confirmation dialog as a substitute for execution-time policy checks.

### F. Run Monitor / playback

This is a critical screen and must prioritize status visibility over decoration.

Header:
- Workflow name.
- Current target application/window.
- State: Preparing, Running, Paused, Waiting for confirmation, Stopping, Completed, Failed, or Cancelled.
- Elapsed time and bounded progress when total steps are known.

Main region:
- Ordered steps with states: Pending, Running, Verified, Failed, Skipped by explicit policy, or Outcome unknown.
- Current action highlighted with icon and text; do not use color alone.
- Verification result after important actions.
- Safe error summary and the time of the failure.

Controls:
- Pause/Resume.
- Stop.
- Emergency stop shortcut hint.
- Confirmation controls when the workflow reaches a configured gate.
- A recovery panel after failure.

Progress rules:
- Progress must reflect real engine state, not an animation.
- If total work is unknown, use an indeterminate indicator and explain why.
- Never show 100% before final verification and cleanup complete.
- Do not promise that Stop instantly interrupts an external operation that cannot be cancelled safely; show “Stopping” until the engine confirms it has stopped.
- After stopping, report whether input state was released and whether the current action's outcome is known.

### G. Run history

Show a filterable table/list with:
- Workflow name.
- Start time.
- Duration.
- Outcome.
- Failed step, if any.
- Recovery status.

Details view:
- Step outcomes and verification records.
- Redacted error message.
- Policy decisions relevant to the run.
- Whether the final state is verified or unknown.
- Recovery choices taken.

Do not log or display full typed payloads, passwords, tokens, clipboard contents, or database connection strings. A “Copy diagnostic summary” feature must produce a redacted summary and show it before copying.

### H. Security

Separate global defaults from workflow-specific permissions.

Sections:
- **Execution policy:** allowed applications/domains, allowed action categories, and confirmation requirements.
- **Sensitive data:** recording exclusions, redaction rules, and secret-storage status.
- **Workflow trust:** imported workflow review and integrity information.
- **Audit history:** policy denials and security-relevant changes.
- **Limitations:** explain that Repliq cannot defend against full compromise of Windows or the user's account.

Use precise status language:
- “Workflow schema valid”
- “Policy checks passed”
- “Target verified”
- “Post-action result verified”
- “Execution completed”

Avoid ambiguous badges such as “100% secure” or a single universal “Safe” score.

### I. Settings

Group settings by user goal:
- General: startup behavior, language if supported, and theme.
- Appearance: Light, Dark, System.
- Shortcuts: recording, playback, pause/resume, and emergency stop.
- Storage: workflow folder, log retention, export, and delete data.
- Privacy: recording exclusions and redaction defaults.
- Performance: diagnostics and optional integrations.
- Accessibility: reduced motion, text scaling compatibility, and keyboard behavior.
- About: version, build information, dependency notices, and support/diagnostics.

Avoid exposing internal implementation flags in ordinary settings. Advanced settings must include concise explanations and safe defaults.

## 12.6 Visual design system

### Overall style

- Native Windows desktop product with a restrained, modern visual language.
- Neutral surfaces; one primary accent; semantic colors reserved for actual status.
- Consistent alignment, spacing, typography, and icon stroke style.
- Avoid excessive cards, gradients, glass effects, glowing outlines, and decorative background animation.
- Do not make security information visually alarming unless there is a real risk requiring immediate attention.

### Color tokens

Define semantic tokens rather than scattering literal colors through code. Suggested starting palette; validate contrast before release.

**Light theme**
- Window background: `#F5F6F8`
- Main surface: `#FFFFFF`
- Secondary surface: `#EEF1F5`
- Primary text: `#1C2430`
- Secondary text: `#586474`
- Border: `#D8DEE7`
- Accent: `#315FCB`
- Success: `#216E45`
- Warning: `#8A5700`
- Error: `#B42318`
- Focus ring: `#245DDA`

**Dark theme**
- Window background: `#14171C`
- Main surface: `#1B2027`
- Secondary surface: `#242B35`
- Primary text: `#F2F4F7`
- Secondary text: `#B1BAC7`
- Border: `#39424F`
- Accent: `#AFC6FF`
- Success: `#8CD9AC`
- Warning: `#F2C66D`
- Error: `#FF9B96`
- Focus ring: `#AFC6FF`

These values are initial design tokens, not proof of sufficient contrast. Test each text/background combination and all control states. Never rely on color alone to communicate success, warning, failure, selection, or disabled state.

### Typography

- Prefer the platform's familiar UI font stack; on Windows, use Segoe UI or the system default.
- Establish a small type scale: page title, section heading, body, secondary label, and compact metadata.
- Body text should remain comfortable to read at normal Windows scaling.
- Avoid very small labels for critical permissions, errors, and confirmation text.
- Support Windows text scaling and verify that layouts do not clip at 125%, 150%, and 200% display scaling.

### Spacing and geometry

Use a consistent spacing system, for example 4, 8, 12, 16, 24, and 32 logical pixels/units as appropriate to the chosen UI framework. Align form labels, inputs, tables, and action buttons. Avoid dense layouts with no separation between destructive and routine controls.

- Standard controls should have comfortable click targets.
- Keep primary action placement consistent within a screen.
- Separate destructive actions from frequently used actions.
- Use familiar Windows focus, hover, disabled, pressed, and selected states.
- Avoid fixed pixel dimensions where layout containers can adapt.

### Icons

- Use one consistent icon set or native system icons.
- Pair unfamiliar icons with visible text labels.
- Give icon-only buttons accessible names and tooltips.
- Do not use an icon as the sole indicator of recording, failure, or security state.

### Motion and performance

- No continuous animated backgrounds or decorative particle effects.
- Use short, optional transitions only when they clarify a state change.
- Respect reduced-motion preferences where feasible.
- Never delay emergency stop, recording stop, or policy denial for an animation.
- Prefer native controls and simple layout containers over a custom GPU-heavy UI.
- Measure GUI overhead and memory; do not assume a theme or framework is lightweight without testing.

## 12.7 Interaction and state model

Every screen and control must define:
- Initial state.
- Enabled/disabled conditions.
- Loading or busy state.
- Success state.
- Validation error state.
- Permission-denied state.
- Cancellation behavior.
- Recovery path.

### Recording states

`Idle → Starting → Recording → Stopping → Review → Saved`

If capture initialization fails, return to Idle with a clear error. If recording reaches a limit, stop safely and preserve only valid, sanitized events. Do not leave the UI showing Recording after capture has stopped.

### Playback states

`Idle → Validating → AwaitingConfirmation (if needed) → Running ↔ Paused → Stopping → Completed / Failed / Cancelled`

- Only the execution controller determines the true state.
- UI state must be driven by engine events, not guessed by the GUI.
- Prevent duplicate Play requests while a run is starting or running.
- Prevent edits to the active workflow while it is executing, or create an explicit immutable run snapshot.
- A cancelled run must not be displayed as successful.
- A failed action with unknown outcome must be visibly different from a verified failure before any change occurred.

## 12.8 HCI and accessibility requirements

Follow WCAG-inspired accessibility practices where applicable to a desktop application and use native Windows accessibility semantics.

- All primary functions must be reachable by keyboard.
- Provide visible focus indication.
- Use a logical tab order and meaningful accessible names/roles.
- Do not trap keyboard focus in custom controls.
- Support keyboard alternatives to drag-and-drop.
- Do not rely on color, position, or sound alone to communicate state.
- Use clear, specific error text with a recovery action.
- Ensure screen readers can identify labels, status, table headers, and current selection.
- Do not steal focus unexpectedly during playback or when background events occur.
- Confirmation dialogs should focus the safest/default action, not the destructive option.
- Keep the emergency-stop shortcut documented and configurable; do not make it depend on focus.
- Test high-contrast settings, display scaling, and common keyboard-only flows.
- Avoid flashing effects and rapidly changing visual indicators.

## 12.9 Error message and notification writing

Use a consistent pattern:

**What happened** — short, concrete statement.  
**Why it matters** — describe the risk or consequence.  
**What Repliq did** — state whether execution stopped and whether the outcome is known.  
**Next action** — offer safe, specific options.

Example:

> **Repliq stopped because the customer field could not be verified.** No further actions were executed. The last completed step was verified, but the current field's state is unknown. Inspect the target application before choosing Resume or Restart.

Avoid:
- “Something went wrong.”
- “Automation failed” with no step information.
- Stack traces in the normal UI.
- Claims that data was saved unless the result was verified.
- A Retry button that repeats a non-idempotent action without checking its prior outcome.

## 12.10 HCI usability testing

Test with representative users, including people unfamiliar with automation tools. Use a test application and synthetic data.

Core tasks:
1. Record a five-step workflow and remove one accidental action.
2. Save, rename, and find a workflow.
3. Identify which application a workflow will affect.
4. Run a dry run and explain the proposed changes.
5. Recognize why a workflow is blocked by policy.
6. Stop a running workflow using the UI and the global emergency shortcut.
7. Interpret a failed verification and choose a safe recovery option.
8. Find and understand a previous run's redacted log.
9. Change a shortcut without losing the emergency-stop capability.
10. Complete core tasks using only the keyboard.

Measure:
- Task completion rate.
- Time on task.
- User errors and unsafe near-misses.
- Whether users can correctly explain current recording/playback state.
- Whether users understand the difference between validated, authorized, and verified.
- Whether users can locate Stop and emergency stop under pressure.
- Perceived clarity and confidence, without treating confidence as proof of safety.

Suggested initial acceptance goals:
- At least 90% of test participants complete core record/save/run tasks without assistance after a brief orientation.
- All participants can identify whether Repliq is recording or running in the tested states.
- All participants can locate the normal Stop control; emergency-stop discoverability is tested separately.
- No critical usability defect permits a user to mistake an unverified or blocked workflow for a verified success.
- All critical keyboard-only flows pass.
- Every critical issue found during testing is fixed or explicitly accepted with documented rationale before release.

These are proposed product targets. The sample size and test method must be recorded; a small usability study does not prove usability for every population.

## 12.11 UI implementation sequence

Build the UI with mock data before enabling real input capture or injection:

1. Design tokens, main window, navigation, and responsive layout.
2. Home and workflow library with mock workflows.
3. Recorder review with mock events.
4. Workflow editor and properties panel.
5. Dry-run/permissions review.
6. Run Monitor with a simulated execution state machine.
7. Run history and recovery details.
8. Security and Settings.
9. Keyboard navigation, accessibility, display scaling, and error-state review.
10. Connect the GUI to tested core interfaces.
11. Enable real recording and playback only after cancellation, policy enforcement, and target-verification tests pass.

At every stage, test idle CPU and memory. Avoid introducing a webview-based UI or large UI framework unless a documented usability requirement cannot be met with the chosen native framework.

## 12.12 Antigravity UI implementation prompt

Use this instruction when implementing the interface:

> Implement Repliq's UI according to Section 12 of `Repliq.md`. Use the selected native Windows UI framework and the existing architecture. Start with mock data and a mock automation backend; do not implement real global hooks or input injection in the UI phase. Build one screen at a time, beginning with the main window, navigation, Home, and Workflows. Use semantic design tokens for light/dark themes, accessible labels, keyboard navigation, clear focus states, and explicit recording/playback state. Do not invent security status: bind status labels to mock states until the real engine exists. Keep the UI responsive, avoid decorative animation and unnecessary dependencies, and test at common Windows display scaling settings. For each screen, implement empty, normal, loading, error, blocked, and success states as applicable. Run tests and report changed files, commands, results, and unresolved issues before proceeding to the next screen.


---

## Revision note

This revision adds a detailed HCI/UI/UX specification in Section 12, including information architecture, screen-level requirements, interaction states, visual tokens, accessibility, error messaging, usability testing, and an Antigravity implementation prompt. The interface must be built with mock data before real input capture/playback is enabled.

---

## 13. Repliq Quick Panel

### 13.1 Purpose

The **Repliq Quick Panel** is a compact, optional floating window for starting and controlling common recording and playback tasks without opening the full main window. The main Repliq window may remain minimized or hidden while the Quick Panel and background application remain available.

The Quick Panel is a second window within the same Repliq application process. It is **not** a separate automation engine, service, or security boundary.

### 13.2 Core UI

Keep the panel small, scannable, and operable without requiring the user to navigate the main application. The initial layout should include:

- Repliq icon and product name.
- Selected-workflow dropdown, showing the workflow that will be played.
- **Record** control, with a clear recording-state indicator.
- **Play** control for the selected workflow.
- **Pause / Resume** control when playback is active.
- **Stop** control for recording or playback, depending on the current state.
- A concise status line such as *Ready*, *Recording*, *Playing — step 4 of 12*, *Paused*, *Needs confirmation*, or *Stopped with an error*.
- A permissions/verification indicator when the workflow needs approval or a target check.
- Pin/unpin (always-on-top) control.
- A link or button to open the relevant page in the main window, such as workflow review, permissions, or run details.

Use clear labels and accessible names; do not rely on color alone to communicate state. Disable controls that are invalid for the current state, and show a brief reason when practical.

### 13.3 State-dependent behavior

| Current state | Available primary actions | Required behavior |
|---|---|---|
| Ready | Record, Play | Record starts a new capture; Play starts the selected, valid workflow. |
| Recording | Pause if supported, Stop | Show that input is being captured. Stopping finalizes the capture into a reviewable draft; it does not silently publish or auto-run it. |
| Playing | Pause, Stop | Show progress and the current step when available. Keep emergency stop available. |
| Paused | Resume, Stop | Resume only after revalidating the workflow and target conditions required by policy. |
| Needs confirmation | Review/Confirm in the main window, Cancel | Do not bypass confirmation just because the request came from the Quick Panel. |
| Error / interrupted | Stop, View details, Retry when safe | Explain what happened; do not blindly repeat a partially completed, non-idempotent action. |

If the workflow needs editing, mapping, permission changes, or detailed review, open the corresponding screen in the main window rather than squeezing complex controls into the panel.

### 13.4 Main-window and background behavior

- Both windows use the same application-level workflow repository, run controller, recorder, policy evaluator, and run-state model.
- Hiding or minimizing the main window must not terminate a recording or playback run.
- Closing the Quick Panel hides it by default; it must not implicitly stop a run or exit Repliq. Provide an explicit Stop action for stopping work.
- Provide a system-tray entry to reopen the Quick Panel or main window and, while a run is active, access a visible Stop action.
- Exiting Repliq must use an explicit exit path. If recording or playback is active, ask the user whether to stop and exit or cancel the exit. Do not leave an orphaned recording/playback process.
- A setting controls whether the Quick Panel opens automatically on startup. Do not force the panel to appear unexpectedly.
- Show an unobtrusive but visible active-run state in the taskbar/tray. Do not use hidden execution as a way to evade user awareness.

### 13.5 Keyboard shortcuts

Support configurable global shortcuts, registered only while Repliq is running. Initial defaults to evaluate are:

- **Ctrl+Shift+R** — Start recording when idle.
- **Ctrl+Shift+P** — Play the selected workflow when idle.
- **Ctrl+Shift+Space** — Pause/resume the active run.
- **Ctrl+Shift+S** — Stop the current recording or playback.
- **Emergency stop** — A separately configurable shortcut that immediately requests the run controller to stop issuing further actions.

These defaults are proposals, not guaranteed conflict-free choices. At setup and whenever a shortcut is changed, detect registration failures and report conflicts clearly. Allow the user to change or disable each shortcut. Never silently steal a shortcut from another application. A failed shortcut registration must not disable the equivalent visible button.

Shortcut callbacks must dispatch commands to the shared run controller; they must not inject input or execute a workflow directly. Stop and emergency stop should remain available even when the main window is hidden. Emergency stop should prioritize halting new automation actions; document that it cannot undo actions already completed or guarantee reversal of an external operation already in progress.

### 13.6 Security and policy parity

The Quick Panel is a convenience surface, not a lower-security mode.

- Route all record, play, pause, resume, stop, and confirmation requests through the same application services and policy checks used by the main window.
- Do not permit workflows to execute arbitrary code, bypass target verification, ignore allowlists, or elevate permissions through Quick Panel controls.
- Enforce workflow validation, target/action allowlists, timeouts, bounded retries, and least-privilege rules regardless of which window initiates the run.
- High-impact or ambiguous actions must use the existing confirmation flow. If a confirmation cannot be safely displayed in the Quick Panel, bring the main window forward and require confirmation there.
- Show enough status to keep the user informed without exposing passwords, tokens, or sensitive form values.
- Ensure logs and notifications use the existing redaction rules.
- Do not record keystrokes from secure desktop/UAC prompts or claim that Repliq can safely automate every protected or unsupported UI.

### 13.7 Lightweight implementation guidance

- Implement the panel using the selected native Windows UI framework and keep it in the existing application process.
- Reuse existing application services and event/state notifications. Do not create a second recorder, playback engine, database connection loop, or frequent polling timer for the panel.
- Prefer event-driven updates for status and progress.
- Register/unregister global shortcuts with application lifecycle management and handle shutdown/unregistration failures safely.
- Keep the system-tray integration and startup behavior optional/configurable where practical.
- Measure memory, idle CPU, startup impact, and package size after implementation; do not describe the panel as “zero overhead” without measurements.

### 13.8 Acceptance criteria

The Quick Panel is acceptable when all of the following are demonstrated:

1. The user can start and stop a recording using the panel or configured shortcut; the result becomes a reviewable draft.
2. The user can select and play a valid workflow, pause/resume it, and stop it from the panel while the main window is hidden.
3. The panel's state and progress stay consistent with the main window and run history.
4. Closing the panel hides it without unexpectedly stopping a run; exiting the application handles active work explicitly.
5. Tray controls can reopen the appropriate window and expose Stop while a run is active.
6. Shortcut conflicts or registration failures are reported, shortcuts are configurable/disableable, and buttons still work if global registration fails.
7. Both windows enforce identical validation, permissions, target checks, confirmations, and logging/redaction policies.
8. Emergency stop prevents additional automation actions as quickly as the controller can safely halt, while clearly communicating that completed actions are not rolled back.
9. Errors, interrupted runs, and retry paths are understandable and do not blindly repeat non-idempotent actions.
10. UI tests cover keyboard navigation, accessible names, display scaling, state transitions, and operation with the main window minimized/hidden.
11. Performance tests record the Quick Panel's incremental memory and idle CPU cost on the target Windows environment.


