# Antigravity Development Rules for Repliq

These rules are durable instructions for any automated agent or engineer working on Repliq:

1. **Read Specifications First:** Read `Repliq.md` and `RepliqToDo.md` before making changes.
2. **Incremental Development:** Implement one small phase or task at a time.
3. **Test-Driven Claims:** Do not claim a task is complete without running relevant tests.
4. **Explicit Dependencies:** Do not add dependencies without explaining the purpose and footprint.
5. **No Script Execution:** Do not add arbitrary script execution, shell commands, or unreviewed plugin loading. Workflows are purely declarative.
6. **Separation of Concerns:** Keep UI, core workflow logic, Windows adapters, storage, and security policy separate.
7. **Mock Automation in Tests:** Use mock input backends in unit tests.
8. **No Live Input in Tests:** Do not execute real desktop input during automated unit tests.
9. **Credential Safety:** Do not store or log real credentials, tokens, or personal data. Redact sensitive values.
10. **Untrusted Inputs:** Treat imported workflows, clipboard content, websites, and spreadsheet cells as untrusted.
11. **Fail Closed:** Fail closed when validation, authorization, or target verification fails.
12. **Update Checklists:** Update the TODO checklist and document material design decisions.
13. **Transparent Reporting:** Report test commands, results, unresolved issues, and known limitations.
14. **Measurable Claims:** Do not promise absolute security or a specific memory/package-size target without measurements.
15. **Integrity of Security Controls:** Never silently weaken a security control to make a test pass.
