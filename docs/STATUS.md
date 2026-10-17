# Status

| Milestone | State |
|---|---|
| v0.1.0 — pipeline, 9 rules, governance CLI, docs, perf budget | code complete, gates green |
| v0.1.1 — adoption upgrade (rule layering, config-gated C# surface, community shell) | code complete, gates green |
| v0.2.0 — remaining 8 rules (DG0004/0006/0008/0009/0011/0013/0014/0017) | code complete, gates green |
| v0.2.x — Tokens reference implementation + token-type matching | planned |

Gates enforced on every change: full test matrix green (net8 + net10),
Core branch coverage ≥ 90%, corpus bootstrap green, rule-doc anti-drift green,
performance budget < 10 s for 10k synthetic XAML.

Docs site: https://zhaojie-tech.github.io/wpf-design-guard/
