# Repliq Release v0.1.0-alpha

**Initial Public Alpha Release**  
**Developer:** [RetsardCo](https://github.com/RetsardCo)  
**Release Date:** 2026-10-14  

---

## What's New in v0.1.0-alpha

Welcome to the initial public Alpha of **Repliq**, a secure, local-first Windows workflow automation and macro playback engine designed for deterministic desktop productivity.

### Highlights & Key Features

1. **Official Logo & Visual Identity**
   - New official application icon featuring the stylized 'R' shield with security padlock badge.
   - High-DPI application icon embedded across `Repliq.App.exe` and `Repliq.Quick.exe`.
   - Native branding in application headers, taskbar, system tray, and about dialog.

2. **UI/UX Dark Theme Contrast & Accessibility Overhaul**
   - **High-Contrast Theming**: Resolved low-contrast button states; disabled/secondary controls now feature clear, accessible `#94A3B8` text on dark slate surfaces with clear borders.
   - **Dark-Themed ListViews**: Custom owner-drawn headers and list rows with vibrant royal blue selection and clear column separators.
   - **Dropdown & Input Polish**: Dropdown menus, textboxes, and numeric spinners properly inherit dark backgrounds and crisp white typography.

3. **Re-engineered Workflow Editor (Modern 2-Column Master-Detail)**
   - Replaced cramped 3-pane layout with an aesthetic 2-column master-detail canvas:
     - **Left Column**: Clean action sequence timeline with step badges, reordering controls (`▲`/`▼`), and disable/enable toggles.
     - **Right Column**: Comprehensive Step Inspector with organized sections:
       1. Action Type & Target Application (Process name, Window title)
       2. Timing & Parameters (Side-by-side delay/timeout, context-sensitive payload inputs)
       3. Safety Policies & Verification (Explicit confirmation, target existence check, real-time safety banner)
   - Real-time safety validation status badge and error reporting.

4. **Dedicated About View**
   - Accessible directly from the sidebar navigation (`ℹ️ About`) and Settings.
   - Prominently showcases developer attribution: **Developed by RetsardCo (2026)**.
   - System diagnostics viewer with one-click clipboard copy.

5. **Dual Distribution Modes**
   - **Repliq Studio (`Repliq.App`)**: Full workflow studio, recorder, editor, and audit log viewer.
   - **Repliq Quick (`Repliq.Quick`)**: Zero-bloat standalone floating toolbar for quick macro recording and instant hotkey playback (Ctrl+Shift+R, Ctrl+Shift+P, Ctrl+Shift+S).

---

## Distribution Packages

| Package | Type | Size | Description |
|---|---|---|---|
| `Repliq-v0.1.0-alpha-Full-Installer.exe` | Windows Installer | ~4.4 MB | Full studio installer with Start Menu & Desktop shortcuts |
| `Repliq-v0.1.0-alpha-Full-Portable.zip` | Portable ZIP | ~2.7 MB | Standalone portable full studio |
| `RepliqQuick-v0.1.0-alpha-Lightweight-Installer.exe` | Windows Installer | ~4.3 MB | Ultra-lightweight standalone toolbar installer |
| `RepliqQuick-v0.1.0-alpha-Lightweight-Portable.zip` | Portable ZIP | ~2.6 MB | Standalone portable toolbar |

### SHA-256 Checksums

```text
1B99541F06C6E6312603AD89D71B8E21E01E1382DCDD5A7A4D988AA16A127887  Repliq-v0.1.0-alpha-Full-Installer.exe
0599EC4CECF645FF6AA4A113EEFA6A5020B84A609E013205B1C728500C0DA544  Repliq-v0.1.0-alpha-Full-Portable.zip
BA67308BD08AF8504F02D024D561A8F3FA38EDEE2D288B90DD8BF19E08F48929  RepliqQuick-v0.1.0-alpha-Lightweight-Installer.exe
95A0CA403DE66111E4054E0139CA1903CFF1BF3930C6C2B6DDBD9173D7EFDEDC  RepliqQuick-v0.1.0-alpha-Lightweight-Portable.zip
```

---

## System Requirements

- **Operating System**: Windows 10 (Build 19041+) or Windows 11 (x64)
- **Runtime**: .NET 8.0 Windows Desktop Runtime
- **Permissions**: Runs entirely as standard user (`asInvoker`), no admin escalation needed.

---

## License

Copyright © 2026 RetsardCo. Distributed under the [MIT License](LICENSE).
