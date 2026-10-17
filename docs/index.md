# WpfDesignGuard

**让设计系统违规在编译期爆红。** WPF 项目的字号令牌、间距阶梯、换行行高、样式继承
与棘轮基线，从「每个项目手写一遍契约测试」变成「装一个 NuGet 包，编译报错 +
Problems 面板反馈 + CI 棘轮门禁」。

## 三件交付物

| 包 | 形态 | 职责 |
|---|---|---|
| `WpfDesignGuard.Analyzers` | NuGet（含 MSBuild targets） | XAML 增量生成器 + C# analyzer，消费 `@(Page)/@(ApplicationDefinition)` 投影 |
| `WpfDesignGuard.Cli` | dotnet tool（`designguard`） | 双层棘轮门禁：verify / init / baseline --update / fix / report (SARIF) |
| `WpfDesignGuard.Core` | 生成器运行库（同目录加载） | 规则目录 + 求值器 + 豁免/硬零/基线——唯一实现，两个引擎零漂移 |

## 反馈面（按宿主分层，如实承诺）

- **dotnet build / CI**：编译输出带 `file(line,col)` 的诊断（error/warning）。
- **VS Code + C# DevKit**（XAML 以文本打开）：Problems 面板 + 点击跳转
  （additional-file 诊断通路）。
- **Visual Studio**：诊断在完整 build 后进入 Error List。
- Visual Studio / Rider 的 **XAML 编辑器不渲染**生成器波浪线（不走 Roslyn 文本管线）——
  这是已知限制，文档与 README 全文一致口径。

## 两层价值

**第一层（开箱即得，零令牌配置）**：五条令牌无关规则查真实缺陷——
DG0012（Setter 不存在，运行时炸）、DG0015（Run.Text 绑定方向，开页炸）、
DG0010（内联样式吞库样式，编译绿界面坏）、DG0016（MaxWidth 塌缩）、
DG0007（换行行高缺失）。装包 + 一条 init，存量共存、新增即时红。

**第二层（接入令牌体系后开启）**：DG0001/0002/0003/0005 管字号/间距/颜色的
令牌化纪律——init 模板默认禁用，配置令牌表后翻开关再重跑 init（ADR-0005）。

## 快速开始

见 [渐进采纳指南](guides/gradual-adoption.md)。五分钟路径：

```bash
dotnet tool install WpfDesignGuard.Cli
designguard init     # 存量命中写入基线（含 per-file 快照），不炸编译
dotnet build         # 存量 = warning；新增违规 = error
designguard verify   # 门禁：硬零域 ∧ 豁免双向相等 ∧ 全局棘轮
```

## 规则

17 条 ID 已一次性分配（永不复用），v0.2.0 起全部实装（9 条随 v0.1.0，8 条随 v0.2.0）。
每条规则的确定性等级与取值口径见 [规则目录](rules/index.md)。
