# ADR-0005: init 模板分层默认与再开启路径

状态：已采纳 ｜ 日期：2026-10-15 

## 背景

采纳性评估确认：令牌依赖规则（DG0001/0002/0003/0005）对无令牌体系的仓库开箱即噪音，
是采纳摩擦的核心；而令牌无关五条（DG0007/0010/0012/0015/0016）条条对应真实事故。
要求：降低摩擦，但**不破坏「rules 未列条目 = 默认开」的引擎契约**，也不破坏
「存量不红、增量才红」的冷启动承诺。

## 决策

1. **分层位置：模板，不是引擎。** 「未列 = 默认开」的引擎语义不动；`designguard init` 生成的配置文本
   为已实现的令牌依赖规则显式写 `"enabled": false`（附如何再开启的注释）。
   「令牌依赖」判定来自 `RuleDescriptor.TokenDependent` 元数据——单一事实源，
   RuleDocGen/文档/模板零漂移。
2. **求值顺序契约：播种用全开配置。** init 的求值与基线播种**永远包含令牌四条**
   （模板 enabled:false 只影响写出层）——存量命中照入基线，日后开启不爆红。
   有单测守卫（播种断言含 DG0001/0002 计数）。
3. **注释存续：锚点后处理，init 与 baseline --update 共用。** ConfigWriter 保持纯
   序列化器（无注释能力）；模板层在确定性输出上做锚点注入。两个命令都走
   `ConfigTemplate.WriteWithTemplate`，注释在 re-baseline 后保留（有测试钉死）。
4. **C# 面同受门控。** DG0001/0005 的经典 analyzer 经 AnalyzerOptions.AdditionalFiles
   读取注入的 designguard.json 并尊重 enabled 标志——同一规则 ID 两个面语义一致，
   模板注释不撒谎。无 config 时按目录默认（开）。
5. **baseline --update 保留禁用规则的旧计数**（禁用期无 findings，否则会把基线清零，
   日后开启即爆红）。
6. **再开启的唯一受支持路径：重跑 `designguard init`。** re-init 保留既有 config 段、
   按当前开关重写基线（等效于接受当前状态为新的诚实基线；`baseline --update` 的
   红树拒绝针对日常门禁，不适用于显式的规则集变更）。**副作用显式化**：re-init 会
   把两次 init 之间新增的违规一并吸收进基线——这是语义的一部分，模板注释、本 ADR、
   渐进采纳指南三处明示。
7. **用户字段不被规范化**：ConfigWriter 回读 `config.Schema`，不再把已有 config 的
   `$schema` 重写为默认 URL。

## 后果

- 开箱体验：装包 + 一条 init（零令牌配置）→ 五条令牌无关规则查真炸；
  令牌四条静默待命，注释即文档。
- 代价：init/baseline 写出多一层模板逻辑（Cli 侧，测试归属 Cli.Tests）；
  re-init 的「吸收增量」语义需要文档反复强调。
- 版本口径：模板策略变更不触碰规则目录默认级，minor 内完成。
