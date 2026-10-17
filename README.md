<div align="center">

# WpfDesignGuard

**Design-system violations that fail the build, not the demo.**

Install, run `designguard init` once, and five token-independent rules start catching
real breakage — no token table, no design-system migration required:

| Catches without any tokens configured | What breaks today |
|---|---|
| **DG0012** Setter on a non-existent property | `ResourceDictionary.DeferrableContent` crash at runtime (XAML compile never checks) |
| **DG0015** `Run.Text` binding without `Mode=OneWay` | Page-load crash against read-only sources (23 production sites) |
| **DG0010** Inline style without `BasedOn` | Library style silently replaced — build green, UI broken |
| **DG0016** `MaxWidth` + left-align collapse | Element collapses to content width (layout rework waves) |
| **DG0007** Wrapped text without line height | Inconsistent row heights in Auto rows |

Once you adopt (or already have) a token system, the token rules
(DG0001/0002/0003/0005 — bare `FontSize`/colors/margins) switch on via the
init template comments.

[![CI](https://github.com/zhaojie-tech/wpf-design-guard/actions/workflows/ci.yml/badge.svg)](./.github/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/WpfDesignGuard.Analyzers.svg)](https://www.nuget.org/packages/WpfDesignGuard.Analyzers)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-blue)](#requirements)

*字体令牌 · 间距阶梯 · 换行行高 · 样式继承 · 棘轮基线 — WPF 设计系统编译期守卫*

</div>

---

## The pain (why this exists)

Real production WPF lines (multi-theme touch POS) grow 15+ hand-written contract-test
gates. Each project rewrites the same scans; violations surface only after the full
90-minute test run; proven rules stay locked in private repos.

**WpfDesignGuard turns those gates into compiler errors:**

| Production incident (translated) | Rule |
|---|---|
| `ResourceDictionary.DeferrableContent` crash: `<Setter Property="HasDropShadow">` written as a CLR property (it's a ContextMenuService attached property) — XAML compile never checks Setter existence | **DG0012** Setter property existence (symbol-resolved) |
| `Run.Text` binding without `Mode=OneWay` — default TwoWay vs read-only source crashes at page load (23 production sites) | **DG0015** |
| `MaxWidth` + `HorizontalAlignment="Left"` collapses to content width (~40px) | **DG0016** |
| Wrapped `TextBlock` without `LineHeight` — inconsistent row heights in Auto rows | **DG0007** |
| Inline `<Button.Style>` without `BasedOn` silently replaces the library style — build green, UI broken | **DG0010** |
| Bare `FontSize` literals fall back to the WPF 12px default (165 real occurrences) | **DG0001 / DG0014** (token layer — enabled once tokens are adopted) |
| Violations quietly creep back between iterations | **dual-layer ratchet** (hard-zero ∧ global baseline) |

## Architecture

```
user WPF project ── @(Page)/@(ApplicationDefinition) ──▶ AdditionalFiles
                                                        │
┌──────────────────────────────┐   ┌────────────────────▼───────────┐
│ WpfDesignGuard.Analyzers     │   │ WpfDesignGuard.Cli (dotnet     │
│ XAML incremental generator + │   │ tool `designguard`)            │
│ C# analyzers + code fixes    │   │ verify · init · baseline       │
└──────────────┬───────────────┘   │ --update · fix · report(SARIF) │
               │  shared rule engine ▼                               │
│  WpfDesignGuard.Core — ONE implementation: rules, scope, exemptions,  │
│  hard-zero domains, ratchet baseline. Zero drift between engines.     │
└────────────────────────────────────────────────────────────────────────┘
```

- **Generator surface**: diagnostics on injected XAML additional files —
  Error List / Problems panel + click-through + build errors.
- **CLI surface**: whole-repo governance; the *same* evaluator runs in both.
- Token table, exemptions, hard-zero domains and the baseline all live in one
  `designguard.json` ([published JSON Schema](docs/schema/v1/designguard.schema.json)).

## Quickstart

```bash
# 1. Governance cold start: stock findings enter the baseline (per-file snapshots);
#    token-dependent rules ship disabled in the generated template (one switch each
#    when you adopt tokens) — token-independent rules are active immediately.
dotnet tool install WpfDesignGuard.Cli
designguard init

# 2. Install the analyzer package
dotnet add package WpfDesignGuard.Analyzers

# 3. Build: stock findings = warnings, NEW violations = errors
dotnet build
```

Real output from a demo repo (paths shortened for display; reproduced by the
corpus fixtures in `samples/`):

```text
$ dotnet build
Views/CheckoutView.xaml(3,6): warning DG0007: TextBlock with TextWrapping='Wrap' ...   # stock, in baseline
Views/NewPanel.xaml(3,6):     error   DG0007: TextBlock with TextWrapping='Wrap' ...   # NEW file: red immediately
Views/NewPanel.xaml(4,27):    error   DG0015: Run.Text binding on '{Binding LastOrder}' has no explicit Mode=OneWay ...

$ designguard fix --rule DG0015
Views/NewPanel.xaml: 1 edit(s)
  + <TextBlock><Run Text="{Binding LastOrder, Mode=OneWay}" /></TextBlock>

$ designguard fix --rule DG0007 --apply
Views/NewPanel.xaml: 1 edit(s)
  + <TextBlock LineHeight="24" LineStackingStrategy="BlockLineHeight" ... />

$ designguard verify
designguard verify: GREEN (0 unexempted within baseline, 0 exempted, 0 hard-zero hits).
```

```bash
# 4. CI gate: hard-zero ∧ exemption bidirectional equality ∧ global ratchet
designguard verify          # exit 0 green / 1 config error / 2 violations / 3 engine error
designguard report --format sarif --output designguard.sarif   # GitHub Code Scanning
```

Feedback surfaces, stated per host: **build/CI errors** everywhere;
**VS Code + C# DevKit** renders the panel live; **Visual Studio** shows diagnostics
in Error List after a full build; VS/Rider XAML editors do not render inline squiggles.

## Governance model

- **Exemption ledger**: per-occurrence (`file + count + reason`), bidirectional
  equality — unregistered hits are red, **stale** registrations are red too.
  Exemptions are debt: visible, countable, cleanable.
- **Hard-zero domains**: hits inside are always red; `baseline --update` never relaxes
  them; exemptions inside them are a configuration error.
- **Global ratchet**: per-rule unexempted counts ≤ baseline; only honest re-baselining
  moves it (a red tree refuses `baseline --update`).

Docs (Chinese, auto-generated rule pages): [zhaojie-tech.github.io/wpf-design-guard](https://zhaojie-tech.github.io/wpf-design-guard) —
[gradual adoption](docs/guides/gradual-adoption.md) · [CI integration](docs/guides/ci-integration.md) ·
[third-party FAQ](docs/guides/third-party-faq.md) · [exemption handbook](docs/guides/exemption-handbook.md) ·
[ADRs](docs/adr/index.md)

## Known limitations (stated, not hidden)

- XAML diagnostics hang on additional files: you get **build errors + Error
  List/Problems panel + click-through**. Inline squiggles depend on the host editor:
  **VS Code + C# DevKit** renders them (XAML opened as text); **Visual Studio / Rider's
  XAML editors do not** (they don't run the Roslyn text pipeline); VS shows the
  diagnostics after a full build.
- Additional files are not editable, so XAML rules have no in-IDE lightbulb; bulk fixes
  come from `designguard fix --dry-run`, C# rules have full code fixes (placeholder
  token references until the v0.2 Tokens package).
- All 17 allocated diagnostic rules are implemented as of v0.2.0 (9 in v0.1.0,
  8 in v0.2.0; new rules ship as minor versions).

## Requirements

- .NET 8 SDK or newer (analyzer tiers ship for Roslyn 4.11 and 4.14).
- Consuming projects: WPF or any project defining `@(Page)` items.

## Contributing

Diagnostic IDs are allocated once and never reused. New rule = minor; rule removal or
default-severity change = major; message text changes go in the CHANGELOG. Every rule
ships with ≥6 table-driven tests, corpus examples and a generated doc page — see
[CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
