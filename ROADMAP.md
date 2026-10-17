# ROADMAP

Ranking criteria for the remaining v0.1.x rules (fixed, apply in order):
**1. incident severity** (runtime crash / silent UI break beats style debt) →
**2. token independence** (usable without a token table) →
**3. fix availability** (mechanically fixable) → **4. implementation cost**.

## v0.2.0 (done) — the rolling batch

All eight remaining rules shipped in one minor, ranked by the criteria above:
DG0017 coverage gate, DG0009 clickable stretch, DG0008 hit-target floor,
DG0014 implicit TextBlock style, DG0011 close glyphs, DG0013 button copy,
DG0004 inline uniform spacing, DG0006 dimension literals.

## v0.2

- `WpfDesignGuard.Tokens` reference implementation (typed dictionaries, UiScale).
- Token-type matching rule (Thickness literals assigned to double-typed properties).
- Real token references replacing the R7 placeholder code fixes.

## v0.1.1 (adoption upgrade — done)

- Rule layering: token-dependent rules ship disabled in the `designguard init`
  template; the five token-independent rules are the out-of-the-box value (ADR-0005).
- C# analyzer surfaces honour the config's enabled flag.
- Community shell: templates, CoC, SUPPORT, this roadmap, Pages deployment.
