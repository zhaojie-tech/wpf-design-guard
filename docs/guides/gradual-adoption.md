# 渐进采纳（冷启动 SOP）

目标：装包不得「即编译死」。几百条存量违规不阻塞编译，新增违规即时红。
v0.1.1 起模板分两层：**令牌无关规则开箱即得**（查真炸），令牌规则待接入后开启。

## 第一步：初始化 + 装包

```bash
dotnet tool install WpfDesignGuard.Cli
designguard init
dotnet add package WpfDesignGuard.Analyzers
```

`init` 做三件事：

1. 生成 `designguard.json`（默认令牌表 = 生产系统实测档位，可整表替换）；
2. 全量求值并把**存量命中写入基线**——全局计数 + **per-file 快照**（求值用全开
   配置，令牌规则的存量也照入基线，日后开启不爆红）；
3. 生成的配置模板中，**令牌依赖规则（DG0001/0002/0003/0004/0005/0006）显式
   disabled 并附注释**；令牌无关五条（DG0007/0010/0012/0015/0016）即时生效。
   硬零域与豁免面的存量**不进**基线：域内存量直接红（修，或显式移出域）。

## 第二步：立即获得的价值（零令牌配置）

装包即查五类真实缺陷：Setter 属性不存在（运行时炸）、Run.Text 绑定方向（开页炸）、
内联样式吞库样式（编译绿界面坏）、MaxWidth 塌缩、换行行高缺失。存量文件以 warning
共存，**新增文件即时 error**——生成器与 CLI 都按文件级快照判定（ADR-0007）：
违规从旧文件「搬家」到新文件同样红，全局总数不变不再是避风港。

## 第三步：推进基线

- 新代码守住五条 → 基线只降不升；
- 顺手清一条存量 → `designguard verify` 确认 → `designguard baseline --update`
  （打印 diff，date 更新；红树会被拒绝）；
- 清理豁免：豁免是负债。stale 豁免 verify 直接红，按提示清理。

## 第四步（可选）：接入设计令牌体系

在 config 中配置 `tokens.fontSizes / tokens.spacingLadder`（或直接使用默认档位），
把令牌六条的 `"enabled"` 翻为 `true`，然后——

```bash
designguard init    # 再次运行：保留你的配置段，按当前开关重写基线
```

**重跑 init 是开启规则的唯一受支持路径**（ADR-0005）：它把当前状态整体重取为诚实
基线。副作用显式化：两次 init 之间新增的违规会被一并吸收进基线——规则集变更时
这是预期行为；日常门禁期请用 `baseline --update`（红树拒绝，不会静默吞债）。

## 作用域先行

`scope` 是一等公民：`include/exclude` 控全局面，`scope.perRule` 控单规则面
（perRule.include **替换**全局 include；exclude 叠加）。先窄后宽。

## 大仓开关

`<DesignGuardDisableXaml>true</DesignGuardDisableXaml>` 整体关闭 XAML 面
（CLI 不受影响）。
