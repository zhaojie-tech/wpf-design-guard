# Contributing to WpfDesignGuard

Thanks for your interest! This document covers the practical rules for contributing.

## Ground rules

1. **Design decisions are not free to improvise.** Implementation follows the rule
   catalog (`WpfDesignGuard.Core/Rules/RuleCatalog.cs` — IDs, severities, determinism)
   and the architecture decision records under `docs/adr/`. If you believe a design
   decision is wrong: open a **new ADR** describing the change and raise it in an
   issue first — decisions are recorded, not improvised.
2. **Diagnostic IDs are never reused.** A new rule gets the next free `DGxxxx` ID, forever.
3. **Adding a rule = minor version bump. Removing a rule or changing a default severity =
   major.** Changing a diagnostic message text must be recorded in `CHANGELOG.md`.
4. Every rule ships with **at least 6 table-driven test cases**
   (positive / negative / boundary / exemption / multi-hit / config-off), corpus examples
   in `samples/`, and an auto-generated rule page in the docs.

## Build & test

```bash
dotnet build
dotnet test                                      # full test suite
dotnet test tests/WpfDesignGuard.Core.Tests \
  /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura   # coverage gate: Core branch ≥ 90%
```

CI matrix: `ubuntu-latest` with .NET 8 and .NET 10 for Core/Analyzers/Cli;
`windows-latest` for the `samples/` corpus build (WPF) and self-bootstrapping checks.
The Linux job must not touch WPF-specific targets.

## Commit discipline

Commits are batched logically (one feature/batch per commit). No "initial commit" dumping
the whole tree, no `--no-verify`.

## Pull requests

- Keep one rule / one mechanism per PR.
- Update `docs/STATUS.md` (the progress ledger) as part of your change.
- New rules must include: the rule implementation, 6+ tests, corpus examples,
  a rule-doc source entry, and a `CHANGELOG.md` entry.

## License

By contributing you agree that your contributions are licensed under the MIT License.
