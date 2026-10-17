# 规则目录

诊断 ID 一次性分配、永不复用。新增规则 = minor 版本；删除规则或改默认严重级 = major；
诊断消息文案变更记 CHANGELOG。**本目录由规则目录自动生成**（tools/RuleDocGen），
禁止与实现漂移。

| ID | 规则 | 默认级 | 确定性 | 面 | 首版 |
|----|------|--------|--------|-----|------|
| [DG0001](DG0001.md) | Bare FontSize literal | Error | Deterministic | generator (AdditionalFiles XAML pipeline) + C# analyzer | 0.1.0 |
| [DG0002](DG0002.md) | Margin/Padding off the spacing ladder | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.1.0 |
| [DG0003](DG0003.md) | Uniform single-value Margin/Padding literal | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.1.0 |
| [DG0004](DG0004.md) | Uniform spacing on inline input elements | Warning | Heuristic | generator (AdditionalFiles XAML pipeline) | 0.2.0 |
| [DG0005](DG0005.md) | Bare color literal | Error | Deterministic | generator (AdditionalFiles XAML pipeline) + C# analyzer | 0.1.0 |
| [DG0006](DG0006.md) | Bare CornerRadius/MinWidth/MaxWidth literal | Error | Deterministic | generator (AdditionalFiles XAML pipeline) + C# analyzer | 0.2.0 |
| [DG0007](DG0007.md) | Wrapped TextBlock missing line height | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.1.0 |
| [DG0008](DG0008.md) | Clickable control below hit-target floor | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.2.0 |
| [DG0009](DG0009.md) | Clickable control stretched across the row | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.2.0 |
| [DG0010](DG0010.md) | Inline style without BasedOn | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.1.0 |
| [DG0011](DG0011.md) | Close glyph instead of close control | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.2.0 |
| [DG0012](DG0012.md) | Style Setter property existence | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.1.0 |
| [DG0013](DG0013.md) | Parenthesis stacking / ellipsis abbreviation in button text | Warning | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.2.0 |
| [DG0014](DG0014.md) | Implicit TextBlock style without base FontSize | Error | Deterministic | CLI (whole-repo view) | 0.2.0 |
| [DG0015](DG0015.md) | Run.Text binding without explicit Mode=OneWay | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.1.0 |
| [DG0016](DG0016.md) | MaxWidth combined with HorizontalAlignment=Left on input elements | Error | Deterministic | generator (AdditionalFiles XAML pipeline) | 0.1.0 |
| [DG0017](DG0017.md) | Hit-target type coverage gate | Error | Deterministic | CLI (whole-repo view) | 0.2.0 |
