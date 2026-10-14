# Repliq — Implementation To-Do List

**Project goal:** Build a secure, safe, local-first, resource-conscious Windows desktop workflow automation application.  
**Developer:** RetsardCo (2026)  
**Version:** 0.1.0-alpha  
**Implementation approach:** C# / .NET, a single chosen native Windows UI framework (WinForms on .NET 8), Windows UI Automation where supported, declarative workflow files, and test-first security controls.

## How to use this checklist

- Complete tasks in order unless a documented dependency requires a change.
- Keep each task small enough to review and test.
- Mark a task complete only after verifying its acceptance criteria.
- Update `Repliq.md` when a requirement or architecture decision changes.
- Use mock automation backends in unit tests. Do not send real input during ordinary unit tests.
- Never test destructive actions against production files, websites, or databases.

---

## UI/UX implementation requirements

Follow Section 12 of `Repliq.md` as the source of truth for the interface. Implement the GUI with mock data and a mock automation backend before connecting real input hooks. The interface must always distinguish workflow validation, permission approval, target verification, and verified execution outcome.

- [x] Implement the main window, persistent navigation, and semantic light/dark design tokens.
- [x] Implement Home and Workflows before advanced editor screens.
- [x] Implement Recorder Review without displaying raw sensitive text by default.
- [x] Implement the Workflow Editor with keyboard alternatives to drag-and-drop.
- [x] Implement Dry Run & Permissions with explicit target and operation scope.
- [x] Implement Run Monitor with truthful execution states and an always-accessible Stop control.
- [x] Implement Run History with redacted details and safe recovery options.
- [x] Implement Security and Settings.
- [x] Add empty, loading, validation-error, blocked, failure, and success states to each applicable screen.
- [x] Test keyboard-only navigation, focus order, screen-reader labels, and visible focus.
- [x] Test at 100%, 125%, 150%, and 200% Windows display scaling (PerMonitorV2 configured).
- [x] Test light and dark themes and verify contrast for text, controls, and status states.
- [x] Verify that no UI state claims success before the engine reports a verified result.
- [x] Verify that emergency stop does not rely on UI focus or an animation completing.
- [x] Conduct usability tests using synthetic data and a dedicated test application.
- [x] Record usability findings and resolve critical confusion before release.

**UI acceptance criteria:** a first-time user can find a workflow, understand its target and permissions, preview its actions, identify recording/playback state, stop execution, and understand a failure without needing to inspect source code.

---

## Phase 0 — Confirm requirements and tooling

- [x] Confirm Windows 10/11 support expectations and test on Windows 11 first.
- [x] Select a currently supported .NET version (.NET 8 LTS - 8.0.426).
- [x] Choose WinForms or WPF; document the decision (WinForms selected for minimal memory footprint and native responsiveness).
- [x] Confirm the MVP is local-first and does not require a cloud account.
- [x] Define the MVP as visible recording, review/editing, saving, and controlled replay.
- [x] Define excluded features: arbitrary workflow code, stealth recording, credential harvesting, bypassing MFA/CAPTCHA, and unattended destructive actions.
- [x] Create Git repository and initial commit.
- [x] Record baseline machine specifications for performance testing.
- [x] Review `Repliq.md` and this checklist before implementation.

**Acceptance criteria:** the stack, MVP boundary, threat model, and initial performance measurement method are documented. No real input capture or injection has been added.

## Phase 1 — Solution structure and engineering rules

- [x] Create `Repliq.sln`.
- [x] Create `Repliq.App`.
- [x] Create `Repliq.Core`.
- [x] Create `Repliq.Windows`.
- [x] Create `Repliq.Storage`.
- [x] Create `Repliq.Integrations` only as a lightweight placeholder or defer it until needed.
- [x] Create test projects for core, security, and integration tests.
- [x] Add `AGENTS.md` with the rules from `Repliq.md`.
- [x] Add dependency/version management.
- [x] Configure formatting, analyzers, and nullable reference types where appropriate.
- [x] Add a basic CI or repeatable local build/test command.
- [x] Verify a clean build from a fresh checkout.

**Acceptance criteria:** clean build succeeds, test projects run, and modules compile without circular dependencies.

## Phase 2 — Non-operational GUI prototype and HCI foundation

- [x] Create main window with Repliq branding.
- [x] Add workflow list.
- [x] Add Record, Stop Recording, Play, Pause, Resume, and Stop controls.
- [x] Add visible recording/playback state.
- [x] Add action timeline using mock actions.
- [x] Add workflow details and settings views.
- [x] Add safe placeholder run-history view.
- [x] Add error and confirmation presentation.
- [x] Ensure the UI remains responsive with simulated long-running work.
- [x] Add UI smoke tests where practical.
- [x] Implement semantic design tokens and a consistent light/dark theme.
- [x] Verify visible focus, keyboard navigation, accessible labels, and display scaling.
- [x] Implement empty, loading, validation-error, blocked, failure, and success states where applicable.
- [x] Confirm recording/playback state is always visible and not communicated by color alone.

**Acceptance criteria:** users can navigate the prototype, inspect mock workflows, understand mock permissions, and see truthful simulated state changes. Core tasks are keyboard accessible. No global hooks or real input injection exist yet.

## Phase 3 — Workflow model and schema validation

- [x] Define workflow and action data models.
- [x] Define an explicit schema version.
- [x] Define a closed set of supported action types.
- [x] Define required fields and allowed values for each action.
- [x] Add unique workflow/action identifiers.
- [x] Add maximum workflow file size (2 MB).
- [x] Add maximum action count (2,000 actions).
- [x] Add string-length and nesting limits (1,024 chars).
- [x] Add bounds for delays, timeouts, retries, and total duration.
- [x] Reject unknown actions and unsupported properties.
- [x] Validate paths and URLs against configured policies.
- [x] Add workflow serialization round-trip tests.
- [x] Add malformed/oversized/deeply nested input tests.
- [x] Ensure invalid workflows cannot execute partially.

**Acceptance criteria:** all invalid or unsupported workflows are rejected before any action can reach an execution backend.

## Phase 4 — Policy engine and execution interfaces

- [x] Define `IAutomationBackend` or equivalent narrow interface.
- [x] Define execution request/result models.
- [x] Define a centralized policy decision interface (`IPolicyEngine`).
- [x] Implement default-deny handling for unknown actions and targets.
- [x] Define permitted target processes/windows.
- [x] Define confirmation requirements for high-impact actions.
- [x] Add a mock backend for tests (`MockAutomationBackend`).
- [x] Ensure policy denial is enforced before backend invocation.
- [x] Add tests for allow/deny decisions and policy failure.
- [x] Ensure imported workflow permissions cannot override application policy.

**Acceptance criteria:** the policy engine can deny an action deterministically, and tests prove the denied action never reaches the backend.

## Phase 5 — Recording engine

- [x] Design event model for mouse buttons, coordinates, keyboard input, scroll, and timing.
- [x] Implement recording state machine using mock event input first.
- [x] Add visible recording indicator and elapsed time.
- [x] Add start/stop controls and cancellation.
- [x] Add bounded event queue and maximum recording duration.
- [x] Filter Repliq's own UI events where practical.
- [x] Define sensitive-field exclusion behavior and limitations.
- [x] Do not capture clipboard contents automatically.
- [x] Add redaction before events reach storage or logs.
- [x] Add recording review and action deletion.
- [x] Add tests for repeated start/stop, event limits, and cancellation.
- [x] Only after review, implement Windows input capture behind a replaceable adapter.
- [x] Test that the app does not collect input when recording is off.

**Acceptance criteria:** recording state is explicit, resource use is bounded, sensitive values are not saved by default, and stop recording works reliably.

## Phase 6 — Playback engine and emergency stop

- [x] Implement sequential playback against the mock backend.
- [x] Implement Play, Pause, Resume, and Stop state transitions.
- [x] Add cancellation token/shared cancellation mechanism.
- [x] Make waits and retry loops cancellation-aware.
- [x] Add per-action timeout and maximum workflow duration.
- [x] Add maximum retry and action count.
- [x] Avoid retries for non-idempotent actions unless explicitly safe.
- [x] Implement emergency-stop registration and conflict reporting.
- [x] Ensure emergency stop does not depend on the GUI thread being free.
- [x] Attempt safe release of simulated held keys/buttons on cancellation.
- [x] Stop on missing targets, unexpected windows, or failed verification.
- [x] Add tests for stop during waits, repeated actions, target loss, and shutdown.
- [x] Implement real input playback only after mock tests pass.

**Acceptance criteria:** cancellation prevents subsequent actions in controlled tests, long waits are interruptible, and failures stop the workflow with a useful redacted report.

## Phase 7 — Storage, configuration, and history

- [x] Define per-user data directory (`%LOCALAPPDATA%\Repliq`).
- [x] Implement workflow save/load.
- [x] Implement safe import/export with full validation.
- [x] Implement SQLite metadata and run-history schema.
- [x] Add migration/version strategy.
- [x] Add bounded log retention and rotation.
- [x] Redact text values and sensitive parameters from logs.
- [x] Add safe error codes rather than dumping sensitive exception payloads.
- [x] Handle corrupt files, interrupted writes, and low disk space (atomic writes with temp files).
- [x] Add deletion controls for workflows and history.
- [x] Ensure secrets are not stored in workflow JSON.
- [x] Add tests for storage failure and serialization compatibility.

**Acceptance criteria:** workflow persistence is reliable, imports are revalidated, logs contain no seeded test secrets, and storage failures do not cause unsafe playback.

## Phase 8 — Windows UI Automation adapter

- [x] Evaluate UI Automation support on a dedicated test application.
- [x] Identify target windows and controls using accessible properties.
- [x] Use control patterns where supported instead of relying only on coordinates.
- [x] Add target identity verification before sensitive actions.
- [x] Add timeouts for element discovery and operations.
- [x] Handle missing, ambiguous, or stale controls by stopping.
- [x] Keep coordinate-based actions explicitly marked as less robust.
- [x] Test window movement, display scaling, focus changes, and resolution changes.
- [x] Document UI Automation limitations for unsupported applications.

**Acceptance criteria:** supported controls can be targeted reliably in the test app; ambiguous targets cause a safe stop.

## Phase 9 — Secret handling and security hardening

- [x] Integrate Windows DPAPI or Credential Manager only when a feature requires saved secrets.
- [x] Ensure credentials are never written to workflow JSON.
- [x] Ensure credentials and test tokens are redacted from logs and exception output.
- [x] Minimize secret lifetime in memory.
- [x] Set appropriate per-user file permissions.
- [x] Add path traversal and URL normalization tests.
- [x] Add malicious workflow import tests.
- [x] Confirm no arbitrary shell, PowerShell, Python, JavaScript, or assembly execution exists.
- [x] Confirm no automatic network activity is required for core functions.
- [x] Add dependency vulnerability review to release checklist.
- [x] Document the realistic security boundary and same-user compromise limitations.

**Acceptance criteria:** security tests show that invalid actions are blocked and seeded secrets do not appear in stored workflows or logs.

## Phase 10 — Spreadsheet integration

- [x] Choose and review a maintained `.xlsx`/CSV processing module.
- [x] Read selected sheets and ranges.
- [x] Define source-column-to-variable mapping.
- [x] Validate required fields and value types.
- [x] Add bounded preview.
- [x] Add dry-run report.
- [x] Add backup/overwrite confirmation.
- [x] Write output to a temporary file and handle replacement safely.
- [x] Report row-level failures without logging sensitive cell values.
- [x] Test empty, malformed, oversized, and unexpected workbook data.

**Acceptance criteria:** a test workbook can be processed with preview and dry-run, and the original is not silently overwritten.

## Phase 11 — Browser integration (deferred)

- [ ] Define supported browser and version assumptions.
- [ ] Use an established browser automation framework only when the user requests this capability.
- [ ] Prefer accessible selectors over screen coordinates.
- [ ] Add domain allowlist and target-domain display.
- [ ] Stop on unexpected navigation or page structure.
- [ ] Avoid storing cookies, session tokens, and passwords.
- [ ] Add confirmation gates for submit/send/purchase/delete actions.
- [ ] Test with a local mock form before any real website.
- [ ] Respect MFA, CAPTCHA, access controls, and site/organizational policies.

**Acceptance criteria:** only allowlisted test domains are automated; unexpected navigation causes a safe stop.

## Phase 12 — Database integration (deferred)

- [ ] Define supported database types and connector choices.
- [ ] Start with read-only connections.
- [ ] Use least-privilege credentials.
- [ ] Parameterize queries.
- [ ] Reject arbitrary SQL embedded in imported workflows.
- [ ] Add target database confirmation.
- [ ] Add preview/dry-run and bounded row counts.
- [ ] Use transactions and rollback where supported for writes.
- [ ] Redact connection strings and result values from logs.
- [ ] Test against a disposable local database.

**Acceptance criteria:** initial database workflows are read-only and use parameterized queries; write operations require a separately reviewed design.

## Phase 13 — Performance and lightweight operation

- [x] Establish a documented reference Windows machine.
- [x] Measure cold and warm startup across at least 10 runs.
- [x] Measure idle CPU over five minutes after settling.
- [x] Measure idle private working set.
- [x] Measure memory after a 1,000-event recording and after release.
- [x] Measure playback for a bounded 500-action mock workflow.
- [x] Verify the GUI remains responsive while playback runs.
- [x] Verify no busy-wait loops or unnecessary idle polling.
- [x] Compare framework-dependent and self-contained package size.
- [x] Evaluate trimming or Native AOT only after compatibility checks.
- [x] Record results and regressions; do not invent performance claims.

**Acceptance criteria:** benchmark results are recorded with environment details, and the app returns near its idle baseline after a workflow ends.

## Phase 14 — Packaging and release

- [x] Create Release build configuration.
- [x] Decide framework-dependent versus self-contained distribution.
- [x] Verify all dependencies and package sources.
- [x] Produce a software bill of materials where practical.
- [x] Define signing and trusted release process.
- [x] If an updater is added, require verified signed updates and fail closed.
- [x] Test installation and first launch on a clean Windows user profile.
- [x] Test upgrade and recovery behavior.
- [x] Scan release artifacts and inspect included files.
- [x] Write user guide, security notes, and troubleshooting guide.
- [x] Verify all critical release gates in `Repliq.md`.

**Acceptance criteria:** a clean-machine test succeeds, critical security tests pass, and published claims are backed by recorded measurements.

## Phase 15 — Final regression and HCI review

- [x] Re-run all unit and integration tests.
- [x] Re-run malformed workflow and secret-redaction tests.
- [x] Verify emergency stop during all supported playback states.
- [x] Verify policy denial prevents backend execution.
- [x] Verify no unexpected network requests from core functionality.
- [x] Verify no elevation prompt during ordinary use.
- [x] Verify workflow import/export compatibility.
- [x] Verify core tasks with keyboard only and inspect focus order.
- [x] Verify text and controls at 100%, 125%, 150%, and 200% display scaling.
- [x] Verify light/dark theme status and text contrast.
- [x] Verify recording, blocked, failed, cancelled, and verified-success states are distinguishable without color alone.
- [x] Verify error messages explain what happened, what is known, and the safe next action.
- [x] Run usability tests with synthetic data and record findings.
- [x] Verify resource use and startup against the recorded baseline.
- [x] Review all known limitations and unresolved risks.
- [x] Update `Repliq.md`, README, and this checklist.
- [x] Tag the release only after all critical acceptance criteria pass.

---

## Quick Panel: Compact Record and Playback Window

### Design and shared architecture
- [x] Add the optional Repliq Quick Panel as a compact floating window in the existing application process.
- [x] Reuse the same workflow repository, recorder, playback/run controller, policy evaluator, and state notifications as the main window.
- [x] Define a single shared run-state model for Ready, Recording, Playing, Paused, Needs Confirmation, Error, and Stopped.
- [x] Add the selected-workflow control and Record, Play, Pause/Resume, and Stop actions.
- [x] Show concise status, progress/current step when available, and a clear permissions/verification indicator.
- [x] Add pin/unpin and a link to open relevant details in the main window.
- [x] Make invalid actions unavailable for the current state and provide concise explanations where useful.

### Background, tray, and lifecycle
- [x] Ensure minimizing/hiding the main window does not interrupt active recording or playback.
- [x] Make closing the Quick Panel hide it by default without silently stopping a run or exiting the application.
- [x] Add a system-tray menu to open the Quick Panel and main window, and expose Stop while a run is active.
- [x] Add explicit application-exit handling when recording or playback is active; prevent orphaned runs.
- [x] Add a user setting for whether the Quick Panel opens automatically at startup.
- [x] Keep active-run status visible in the tray/taskbar without hiding automation activity from the user.

### Global keyboard shortcuts
- [x] Implement configurable global shortcuts for Record, Play, Pause/Resume, Stop, and Emergency Stop.
- [x] Evaluate proposed defaults: Ctrl+Shift+R, Ctrl+Shift+P, Ctrl+Shift+Space, and Ctrl+Shift+S; make every shortcut configurable and disableable.
- [x] Detect shortcut registration failures/conflicts and explain the issue without silently taking over another application's shortcut.
- [x] Keep equivalent panel buttons operational when global shortcut registration fails.
- [x] Route shortcut callbacks through the shared run controller; do not let shortcut handlers execute workflows directly.
- [x] Verify Stop and Emergency Stop work while the main window is hidden.
- [x] Document emergency-stop limits: stop issuing new actions promptly, but do not promise rollback of completed or externally in-progress operations.

### Security and policy parity
- [x] Apply the same workflow validation, allowlists, target verification, permissions, confirmations, timeouts, retry limits, and logging/redaction rules from both windows.
- [x] Ensure the Quick Panel cannot bypass arbitrary-code restrictions or create a less restrictive execution path.
- [x] Route complex/high-impact confirmations to the main window when they cannot be presented safely in the panel.
- [x] Ensure status text and notifications do not reveal credentials or sensitive captured values.
- [x] Confirm protected/unsupported UI limitations are handled explicitly and not misrepresented.

### Testing and performance
- [x] Test every Quick Panel state transition and ensure the main window, panel, and run history remain synchronized.
- [x] Test recording creates a reviewable draft and does not silently auto-run the newly captured workflow.
- [x] Test hidden/minimized main-window operation, panel hide/restore, tray access, and application exit during active work.
- [x] Test shortcut conflicts, failed registration, user-configured shortcuts, and button fallback behavior.
- [x] Test policy parity and confirmation enforcement from panel buttons and global shortcuts.
- [x] Test interrupted runs and confirm retry does not blindly repeat non-idempotent actions.
- [x] Test emergency stop during recording, between playback steps, and during a slow action.
- [x] Test keyboard-only use, accessible names, screen-reader status, and Windows display scaling.
- [x] Measure incremental RAM, idle CPU, startup impact, and package-size impact; document results rather than assuming negligible cost.
