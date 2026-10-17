# ADR-0001: 求值器单源与文件集口径

状态：已采纳 ｜ 日期：2026-10-15

## 背景

XAML 诊断走生成器（AdditionalFiles 面），棘轮与基线治理走 CLI。若两侧各写一套规则
求值，「同一规则两套算法漂移」必然发生（生产环境用血换来的纪律：两套算法漂移会让
CI 绿而 IDE 红或反之）。

## 决策

1. 规则求值器只在 `WpfDesignGuard.Core` 实现一次：`XamlRuleEngine.Evaluate` 是唯一
   入口，生成器与 CLI 逐字共用（纯函数：文本 + 相对路径 + 配置 → 命中列表）。
2. 治理层（豁免/硬零/棘轮/冷启动降级）同样单源（`GovernanceEvaluator`、
   `ColdStartPolicy`），生成器侧的 Warning/Error 降级与 CLI 的门禁判定同引擎。
3. **文件集口径如实区分**（不宣称「必然一致」）：生成器面 = MSBuild
   `@(Page)/@(ApplicationDefinition)` 投影；CLI 面 = scope include/exclude。
   `verify --project` 经包内 targets 显式导出投影清单并列出两面差集——差异可见即可审计。

## 后果

- 规则新增只写一处；两侧行为漂移在结构上不可能。
- 单测直接打 Core（表驱动），生成器/CLI 测试只验证接线。
- targets 投影必须用 `%(FullPath)`：MSBuild 以构建工作目录（非项目目录）交付
  item 相对路径，相对化会破坏 per-file 快照键（实施期实证 bug，回归测试锁定）。
