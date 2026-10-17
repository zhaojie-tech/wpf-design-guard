using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Rules;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class ConfigLoaderTests
{
    private const string MinimalConfig = """
        {
          "$schema": "https://zhaojie-tech.github.io/wpf-design-guard/schema/v1/designguard.schema.json",
          "version": 1
        }
        """;

    [Fact]
    public void MinimalConfig_LoadsWithDefaults()
    {
        var result = ConfigLoader.LoadFromText(MinimalConfig);
        Assert.False(result.HasErrors);
        var config = result.Config!;
        Assert.Equal(48, config.Tokens.HitTargetFloor);
        Assert.Equal(16, config.Tokens.FontSizes["Body"]);
        Assert.Empty(config.HardZero);
        Assert.Empty(config.Exemptions);
        Assert.Null(config.Baseline);
        Assert.Equal(".", config.Scope.Root);
        Assert.True(config.IsEnabled("DG0002"));
        Assert.Equal(RuleSeverity.Error, config.SeverityOf("DG0002"));
        Assert.Equal(RuleSeverity.Warning, config.SeverityOf("DG0004")); // catalog default
    }

    [Fact]
    public void JsonComments_AndTrailingCommas_AreAccepted()
    {
        const string jsonc = """
        {
          // single line
          "version": 1,
          /* block
             comment */
          "rules": { "DG0002": { "severity": "warning", }, },
        }
        """;
        var result = ConfigLoader.LoadFromText(jsonc);
        Assert.False(result.HasErrors);
        Assert.Equal(RuleSeverity.Warning, result.Config!.SeverityOf("DG0002"));
    }

    [Theory]
    [InlineData("""{ "version": 2 }""", "DGCFG_VERSION")]
    [InlineData("""{ "version": "1" }""", "DGCFG_VERSION")]
    [InlineData("""{ }""", "DGCFG_VERSION")]
    public void VersionValidation(string json, string expectedCode)
    {
        var result = ConfigLoader.LoadFromText(json);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Code == expectedCode);
    }

    [Fact]
    public void MalformedJson_ReportsParseError()
    {
        var result = ConfigLoader.LoadFromText("{ version: 1 nope }");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_PARSE");
        Assert.Null(result.Config);
    }

    [Fact]
    public void NonObjectRoot_ReportsShapeError()
    {
        var result = ConfigLoader.LoadFromText("[]");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SHAPE");
    }

    // ---------------------------------------------------------- tokens floors

    [Fact]
    public void BodyFontSize_BelowFloor_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "fontSizes": { "Body": 14 } } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_TOKENS_FLOOR" && e.Message.Contains("Body"));
    }

    [Fact]
    public void MissingBodyFontSize_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "fontSizes": { "Title": 20 } } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_TOKENS_FLOOR" && e.Message.Contains("Body"));
    }

    [Fact]
    public void HitTargetFloor_Below44_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "hitTargetFloor": 40 } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_TOKENS_FLOOR" && e.Message.Contains("hitTargetFloor"));
    }

    [Fact]
    public void HitTargetFloor_44_IsAccepted()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "hitTargetFloor": 44 } }""");
        Assert.False(result.HasErrors);
        Assert.Equal(44, result.Config!.Tokens.HitTargetFloor);
    }

    [Fact]
    public void EmptyLadder_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "spacingLadder": [] } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_TOKENS_LADDER");
    }

    [Fact]
    public void UnsortedLadder_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "spacingLadder": [8, 4] } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_TOKENS_LADDER" && e.Message.Contains("ascending"));
    }

    [Fact]
    public void NegativeLadderEntry_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "spacingLadder": [-2] } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_TOKENS_LADDER" && e.Message.Contains("negative"));
    }

    [Fact]
    public void TokenTableReplacesWhole_DefaultsSurviveOnlyWhereUnspecified()
    {
        const string json = """
        {
          "version": 1,
          "tokens": {
            "fontSizes": { "Body": 16, "Heading": 24 },
            "uniformSpacingKeys": ["Gap.*"]
          }
        }
        """;
        var config = ConfigLoader.LoadFromText(json).Config!;
        Assert.Equal(16, config.Tokens.FontSizes["Body"]);
        Assert.Equal(24, config.Tokens.FontSizes["Heading"]);
        Assert.False(config.Tokens.FontSizes.ContainsKey("Caption")); // whole-table replacement
        Assert.Equal(new[] { "Gap.*" }, config.Tokens.UniformSpacingKeys);
        Assert.Equal(TokenTable.Default.HitTargetFloor, config.Tokens.HitTargetFloor); // untouched default
        Assert.Equal("App/Themes/Controls.xaml", config.Tokens.StyleLibrary ?? "App/Themes/Controls.xaml"); // styleLibrary null default
    }

    // ---------------------------------------------------------- rules

    [Fact]
    public void UnknownRuleId_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "rules": { "DG9999": { "enabled": false } } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_RULE_UNKNOWN");
    }

    [Fact]
    public void BadSeverity_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "rules": { "DG0002": { "severity": "fatal" } } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_RULES_SEVERITY");
    }

    [Fact]
    public void Severity_IsCaseInsensitive()
    {
        var config = ConfigLoader.LoadFromText("""{ "version": 1, "rules": { "DG0002": { "severity": "Warning" } } }""").Config!;
        Assert.Equal(RuleSeverity.Warning, config.SeverityOf("DG0002"));
    }

    [Fact]
    public void DisabledRule_IsReflected()
    {
        var config = ConfigLoader.LoadFromText("""{ "version": 1, "rules": { "DG0002": { "enabled": false } } }""").Config!;
        Assert.False(config.IsEnabled("DG0002"));
        Assert.True(config.IsEnabled("DG0007"));
    }

    [Fact]
    public void NonBooleanEnabled_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "rules": { "DG0002": { "enabled": "yes" } } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_RULES_ENABLED");
    }

    // ---------------------------------------------------------- scope

    [Fact]
    public void ScopePerRule_IsParsed()
    {
        const string json = """
        {
          "version": 1,
          "scope": {
            "root": "src",
            "include": ["App/**/*.xaml"],
            "exclude": ["App/Themes/**"],
            "perRule": { "DG0001": { "include": ["App/Ui/**"] } }
          }
        }
        """;
        var config = ConfigLoader.LoadFromText(json).Config!;
        Assert.Equal("src", config.Scope.Root);
        Assert.Equal("App/Ui/**", config.Scope.PerRule["DG0001"].Include![0]);
    }

    [Fact]
    public void EmptyInclude_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "scope": { "include": [] } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SCOPE_EMPTY");
    }

    // ---------------------------------------------------------- hard zero

    [Fact]
    public void HardZero_IsParsedWithNormalizedPaths()
    {
        const string json = """
        { "version": 1, "hardZero": { "App\\Pos\\Register\\Print\\**": ["DG0002", "DG0007"] } }
        """;
        var config = ConfigLoader.LoadFromText(json).Config!;
        Assert.Equal(new[] { "DG0002", "DG0007" }, config.HardZero["App/Pos/Register/Print/**"]);
    }

    [Fact]
    public void HardZeroUnknownRule_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "hardZero": { "**/Print/**": ["DG0042"] } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_RULE_UNKNOWN");
    }

    // ---------------------------------------------------------- baseline

    [Fact]
    public void Baseline_WithPerFileSnapshot_IsParsed()
    {
        const string json = """
        {
          "version": 1,
          "baseline": {
            "date": "2026-11-01",
            "counts": { "DG0002": 36, "DG0007": 199 },
            "files": { "Views/LegacyShell.xaml": { "DG0002": 12, "DG0007": 41 } }
          }
        }
        """;
        var config = ConfigLoader.LoadFromText(json).Config!;
        Assert.NotNull(config.Baseline);
        Assert.Equal("2026-11-01", config.Baseline!.Date);
        Assert.Equal(36, config.Baseline.Counts["DG0002"]);
        Assert.Equal(41, config.Baseline.Files["Views/LegacyShell.xaml"]["DG0007"]);
    }

    [Fact]
    public void NegativeBaselineCount_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "baseline": { "counts": { "DG0002": -1 } } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_BASELINE");
    }

    // ---------------------------------------------------------- exemptions

    private const string ConfigWithScope = """
        {
          "version": 1,
          "scope": { "include": ["App/**/*.xaml"], "exclude": ["App/Themes/**"] }
        }
        """;

    [Fact]
    public void Exemption_MissingReason_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText(
            ConfigWithScope.Replace("}", """
                , "exemptions": [ { "rule": "DG0009", "file": "App/Views/X.xaml", "count": 1 } ] }
                """));
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_EXEMPTIONS_REASON");
    }

    [Fact]
    public void Exemption_CountBelowOne_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText(
            ConfigWithScope.Replace("}", """
                , "exemptions": [ { "rule": "DG0009", "file": "App/Views/X.xaml", "count": 0, "reason": "r" } ] }
                """));
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_EXEMPTIONS" && e.Message.Contains("count"));
    }

    [Fact]
    public void Exemption_OutsideScope_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText(
            ConfigWithScope.Replace("}", """
                , "exemptions": [ { "rule": "DG0009", "file": "Lib/Views/X.xaml", "count": 1, "reason": "r" } ] }
                """));
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_EXEMPTIONS_SCOPE");
    }

    [Fact]
    public void Exemption_InExcludedPath_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText(
            ConfigWithScope.Replace("}", """
                , "exemptions": [ { "rule": "DG0009", "file": "App/Themes/Buttons.xaml", "count": 1, "reason": "r" } ] }
                """));
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_EXEMPTIONS_SCOPE");
    }

    [Fact]
    public void Exemption_InHardZeroDomain_IsConfigError()
    {
        var result = ConfigLoader.LoadFromText("""
        {
          "version": 1,
          "scope": { "include": ["**/*.xaml"] },
          "hardZero": { "App/Pos/Register/Print/**": ["DG0002"] },
          "exemptions": [ { "rule": "DG0002", "file": "App/Pos/Register/Print/Receipt.xaml", "count": 2, "reason": "legacy" } ]
        }
        """);
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_EXEMPTIONS_HARDZERO");
    }

    [Fact]
    public void Exemption_WithType_IsAccepted()
    {
        var result = ConfigLoader.LoadFromText(
            ConfigWithScope.Replace("}", """
                , "exemptions": [ { "rule": "DG0017", "file": "App/Views/X.xaml", "count": 3, "reason": "template internals", "type": "RepeatButton" } ] }
                """));
        Assert.False(result.HasErrors);
        Assert.Equal("RepeatButton", result.Config!.Exemptions[0].Type);
    }

    [Fact]
    public void AllErrors_AreCollected_NotFailFast()
    {
        var result = ConfigLoader.LoadFromText("""
        {
          "version": 1,
          "rules": { "DG9999": {} },
          "tokens": { "hitTargetFloor": 10 },
          "exemptions": [ { "rule": "DG0009", "file": "nope.xaml", "count": 0 } ]
        }
        """);
        Assert.True(result.HasErrors);
        var codes = result.Errors.Select(e => e.Code).ToList();
        Assert.Contains("DGCFG_RULE_UNKNOWN", codes);
        Assert.Contains("DGCFG_TOKENS_FLOOR", codes);
        Assert.Contains("DGCFG_EXEMPTIONS", codes);
        Assert.Contains("DGCFG_EXEMPTIONS_REASON", codes);
        Assert.True(codes.Count >= 4);
    }
}
