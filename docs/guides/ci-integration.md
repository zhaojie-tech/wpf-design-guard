# CI 集成

## GitHub Actions：棘轮门禁

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.0.x'
- run: dotnet tool install --global WpfDesignGuard.Cli
- run: designguard verify --root src/YourApp
```

退出码契约：`0` 绿 · `1` 配置错误（含令牌表下限违反、扫描面防空转、豁免簿非法）·
`2` 违规/超基线/超文件快照 · `3` 引擎内部错误。

门禁是双层的：全局每规则计数 ≤ 基线，**且**每个文件的未豁免命中 ≤ 该文件快照
（快照外文件从 0 起算）。后者保证「违规搬家」也会红（ADR-0007）。

## GitHub Code Scanning（SARIF）

```yaml
- run: designguard report --format sarif --output designguard.sarif
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: designguard.sarif
```

SARIF 携带每条命中的处置（exempted / hard-zero / unexempted）与 stale 豁免、
棘轮击穿，Code Scanning 里全部可见。

## 注意

- `verify --project`（生成器面 vs CLI 面文件集差集对拍）需要 Windows：经
  `dotnet msbuild` 读取包内 targets 的投影清单。
- 语料库自举（防规则引擎自腐化）随包仓库的 CI 走双断言：文本引擎对
  `expected-findings.txt` 逐条相等；DG0012 符号层单独断言。
