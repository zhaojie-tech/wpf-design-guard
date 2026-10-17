# Changelog

All notable changes are documented here. Diagnostic IDs and message texts are public
API: adding a rule = minor; removing a rule or changing a default severity = major;
message text changes are recorded in this file.

## [0.2.2] — 2026-10-17

### Tooling

- NuGet publishing now uses Trusted Publishing (OIDC) in the release workflow,
  with the API key kept as a fallback. No functional changes.

## [0.2.1] — 2026-10-17

### Fixed

- CLI package ID is `WpfDesignGuard.Cli` (not the internal assembly name
  `designguard`), matching the documented install command. The tool command
  remains `designguard`. Supersedes the CLI asset in the v0.2.0 dry-run release.

## [0.2.0] — 2026-10-17

### Rules shipped (all 17 allocated IDs now implemented)

- DG0004 uniform spacing on inline input elements (heuristic, Warning).
- DG0006 bare CornerRadius/MinWidth/MaxWidth literals (generator + C# analyzer;
  detection only — no automatic fix, the token table has no width/radius ladder).
- DG0008 hit-target floor on clickable controls (explicit instance dimensions only;
  style-level floors belong to DG0017).
- DG0009 clickable control stretched across the row (exemptions consume exceptions).
- DG0011 close glyphs (× ✕ ✖) instead of the close button component.
- DG0013 button copy: parenthesis stacking + ellipsis abbreviation (no auto-fix).
- DG0014 implicit TextBlock style missing base FontSize — CLI-only whole-repo rule
  (App.xaml MergedDictionaries chain, last definition wins, BasedOn followed).
- DG0017 clickable-type coverage gate — CLI-only: every used clickable type needs a
  MinHeight/MinWidth >= floor setter at the style library; type-scoped exemptions.

### Changed

- Version 0.2.0 (new rules = minor). Tokens runtime remains deferred.
- CLI ratchet now enforces file-level baseline snapshots in addition to global counts
  (ADR-0007): a violation moved from a legacy file into a new file is red even though
  the total count is unchanged; `baseline --update` refuses over-snapshot trees.
  Existing baselines keep working — init already writes the per-file snapshots.
- DG0006 fix support: CLI fix → none (ADR-0006).
- The generator no longer reports DG0012 as unchecked surface (it is implemented by
  the symbol stage, not the text engine).

## [0.1.1] — 2026-10-16

Adoption upgrade: token-dependent rules disabled in the init template (ADR-0005),
config-gated C# surfaces, community shell, docs reorder. No rule behaviour changes.

## [0.1.0] — 2026-10-15 (release candidate; publish pending maintainer prerequisites)

### Rules shipped (9 of 17 allocated)

- DG0001 bare FontSize literals (XAML generator + C# analyzer, C# code fix with
  placeholder token reference).
- DG0002 Margin/Padding components off the spacing ladder (per-component findings,
  span-level fix: nearest ladder step, tie → smaller).
- DG0003 uniform single-value Margin/Padding literals (fix: directional N,N form).
- DG0005 bare color hex literals (XAML generator) + Colors.*/Color.FromRgb (C# analyzer,
  placeholder fix).
- DG0007 wrapped TextBlock missing LineHeight/LineStackingStrategy (fix: lineHeights
  map + BlockLineHeight).
- DG0010 inline style without BasedOn.
- DG0012 Style Setter property existence via symbol resolution (XmlnsDefinition +
  GetTypeByMetadataName/GetTypesByMetadataName fallback; memoized two-stage pipeline;
  blind spots surface as unchecked, never false positives).
- DG0015 Run.Text binding without explicit Mode=OneWay (fix: span insertion).
- DG0016 MaxWidth + HorizontalAlignment=Left on inline input types.

DG0004/0006/0008/0009/0011/0013/0014/0017 are allocated and catalogued; they ship in
0.1.x minors. The engine surfaces unshipped rules as unchecked (never silent).

### Packages

- WpfDesignGuard.Analyzers 0.1.0 — analyzer tiers roslyn4.11 + roslyn4.14,
  build/+buildTransitive/ projection targets, cold-start safe (stock findings
  downgrade to warnings; config missing/invalid self-disables with DGCFG001/002).
- WpfDesignGuard.Cli 0.1.0 (dotnet tool `designguard`) — init/verify/baseline --update/
  fix/report; exit codes 0/1/2/3.
- WpfDesignGuard.Core 0.1.0 — shared rule engine (single source, zero drift).

### Governance

- designguard.json v1 (JSONC) + published JSON Schema; token-table floors
  (Body ≥ 16, hitTargetFloor ≥ 44) enforced as configuration errors.
- Dual-layer ratchet: hard-zero domains ∧ global baseline; per-occurrence exemptions
  with bidirectional equality (stale = red); exemptions and hard-zero mutually exclusive.
- Anti-idle self-check (empty scan surface = exit 1); red trees refuse baseline --update.
- SARIF 2.1.0 reporting (GitHub Code Scanning shape) + Markdown report.

### Known limitations

See README "Known limitations" — VS/Rider XAML editors do not render additional-file
squiggles; VS shows diagnostics after full build; 8 of 17 rules ship later in 0.1.x.

[0.1.0]: https://github.com/zhaojie-tech/wpf-design-guard/releases/tag/v0.1.0
