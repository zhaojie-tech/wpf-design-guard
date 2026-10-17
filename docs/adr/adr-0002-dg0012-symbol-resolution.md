# ADR-0002: DG0012 符号解析路线

状态：已采纳 ｜ 日期：2026-10-15

## 背景

Style Setter 属性存在性检查（防 DeferrableContent 运行时炸）需要类型信息。早期方案
「加载引用程序集反射」与「CLI 无代码执行」自相矛盾，且编译期加载用户程序集不可行
（R1 评审裁决推翻）。

## 决策

符号层，全程不加载/不执行任何用户代码：

1. xmlns URI → clr-namespace：扫描引用程序集的
   `[assembly: System.Windows.Markup.XmlnsDefinition]`（`IAssemblySymbol.GetAttributes`）；
   `clr-namespace:` 前缀直接映射（从 XAML 文本声明收集，按 compilation 累积）。
2. 类型解析：`Compilation.GetTypeByMetadataName`；重名类型返回 null 的场景以
   `GetTypesByMetadataName` 兜底（R5 外部核验：BCL 复制类型会静默失明）。
3. 存在性：沿基类链查 CLR 属性或 `*Property` 依赖属性字段；附加属性解析 OWNER 类型
   （`TextBlock.FontSize` 的 TextBlock）并要求 `<Name>Property` 字段。
4. **两段增量管线**：文本段产出「待校验 (类型, 属性) 集」（随文本缓存，廉价）；
   类型查询以 compilation + 名字为键 memo（`ConditionalWeakTable`），解析调用计数
   可断言 memo 生效。
5. 失明即降级：未知类型 / 未映射命名空间 → 计入未检面（不红、不豁免、报告可见，R2）。

## 后果

- 覆盖面强于内置 WPF 类型表（第三方/自研类型同样可解析），且无需维护内置表。
- 非 WPF 编译环境（无 XmlnsDefinition 命中）整体进入未检面——不产生误报。
- 测试以 clean-room 模拟程序集（自声明同名 XmlnsDefinitionAttribute）驱动，不依赖 WPF。
