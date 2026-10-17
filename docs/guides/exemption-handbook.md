# 豁免操作手册

豁免是**逐处登记的负债**：`file + count + reason`，禁目录级；`reason` 为空即配置错误。

## 登记

```json
"exemptions": [
  { "rule": "DG0009", "file": "Views/ModeSwitchDialog.xaml", "count": 4,
    "reason": "例外①纵向互斥选项列表≥3项，整块即选项" }
]
```

覆盖闸（DG0017）可加 `"type": "RepeatButton"` 按类型豁免。

## 双向相等（核心语义）

- 扫到**未登记**的违规 → 红；
- 登记了但当前**扫不到**（count 不匹配 / 文件已改）→ **stale 豁免 = 红**，提示清理。

豁免清零不会「释放」基线空间：已豁免命中从不进基线，基线只度量未豁免面。

## 与硬零域互斥

豁免指向硬零域内文件 = 配置错误（exit 1）。两机制互斥：要么修，要么把文件移出硬零域。

## 清理流程

1. `designguard verify` 列出全部 stale 豁免（登记数 vs 实际数）；
2. 文件已改/已修 → 直接删除豁免条目（代码与豁免同 diff 可见）；
3. count 不匹配 → 改 count 或修代码；XAML 内**不允许**行内豁免（无 pragma 是特性不是缺陷）。
