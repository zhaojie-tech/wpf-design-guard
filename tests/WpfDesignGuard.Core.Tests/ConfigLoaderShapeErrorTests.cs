using System.Linq;
using WpfDesignGuard.Core.Configuration;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

/// <summary>
/// Shape-error matrix for ConfigLoader: every malformed section must produce a typed
/// DGCFG_* error instead of throwing, so the generator can self-disable with a clear
/// message and the CLI can exit 1 with actionable output.
/// </summary>
public class ConfigLoaderShapeErrorTests
{
    private static string Code(string json, string section) =>
        ConfigLoader.LoadFromText(json).Errors.FirstOrDefault(e => e.Message.Contains(section))?.Code
            ?? ConfigLoader.LoadFromText(json).Errors[0].Code;

    private static bool HasCode(string json, string code)
    {
        return ConfigLoader.LoadFromText(json).Errors.Any(e => e.Code == code);
    }

    // ---------------------------------------------------------------- scope

    [Theory]
    [InlineData("""{ "version": 1, "scope": [] }""")]
    [InlineData("""{ "version": 1, "scope": { "root": 5 } }""")]
    [InlineData("""{ "version": 1, "scope": { "root": "  " } }""")]
    [InlineData("""{ "version": 1, "scope": { "include": "**/*.xaml" } }""")] // include not an array
    [InlineData("""{ "version": 1, "scope": { } }""")]                       // include missing
    [InlineData("""{ "version": 1, "scope": { "include": [1] } }""")]        // non-string entry
    [InlineData("""{ "version": 1, "scope": { "include": ["a"], "exclude": 5 } }""")]
    public void Scope_ShapeErrors_AreCollected(string json)
    {
        var result = ConfigLoader.LoadFromText(json);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void PerRule_MalformedEntries_AreCollected()
    {
        var result = ConfigLoader.LoadFromText("""
        {
          "version": 1,
          "scope": {
            "include": ["**/*.xaml"],
            "perRule": { "DG0001": ["not", "an", "object"] }
          }
        }
        """);
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SCOPE");
    }

    // ---------------------------------------------------------------- rules

    [Fact]
    public void Rules_NotAnObject_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "rules": [] }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_RULES");
    }

    [Fact]
    public void RuleEntry_NotAnObject_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "rules": { "DG0002": "warning" } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_RULES");
    }

    // ---------------------------------------------------------------- tokens

    [Fact]
    public void Tokens_NotAnObject_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": [] }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_TOKENS");
    }

    [Fact]
    public void HitTargetFloor_NotANumber_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "hitTargetFloor": "big" } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_TOKENS");
    }

    [Fact]
    public void FontSizes_Malformed_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "fontSizes": [] } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SHAPE");
    }

    [Fact]
    public void FontSizes_NonNumberEntry_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "fontSizes": { "Body": "16" } } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SHAPE");
    }

    [Fact]
    public void SpacingLadder_NotAnArray_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "spacingLadder": 8 } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SHAPE");
    }

    [Fact]
    public void SpacingLadder_NonNumberEntry_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "spacingLadder": ["8"] } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SHAPE");
    }

    [Fact]
    public void UniformSpacingKeys_NotAnArray_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "uniformSpacingKeys": "Spacing.*" } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SHAPE");
    }

    [Fact]
    public void LineHeights_Malformed_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "lineHeights": [] } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SHAPE");
    }

    [Fact]
    public void ClickableTypes_NotAnArray_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "clickableTypes": "Button" } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_SHAPE");
    }

    [Fact]
    public void StyleLibrary_Empty_IsCollected()
    {
        var result = ConfigLoader.LoadFromText("""{ "version": 1, "tokens": { "styleLibrary": " " } }""");
        Assert.Contains(result.Errors, e => e.Code == "DGCFG_TOKENS");
    }

    // ---------------------------------------------------------------- hard zero

    [Fact]
    public void HardZero_Malformed_IsCollected()
    {
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "hardZero": [] }""").Errors,
            e => e.Code == "DGCFG_HARDZERO");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "hardZero": { "A/**": "DG0002" } }""").Errors,
            e => e.Code == "DGCFG_HARDZERO");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "hardZero": { "A/**": [1] } }""").Errors,
            e => e.Code == "DGCFG_HARDZERO");
    }

    // ---------------------------------------------------------------- baseline

    [Fact]
    public void Baseline_Malformed_IsCollected()
    {
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "baseline": [] }""").Errors,
            e => e.Code == "DGCFG_BASELINE");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "baseline": { "date": 5 } }""").Errors,
            e => e.Code == "DGCFG_BASELINE");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "baseline": { "counts": [] } }""").Errors,
            e => e.Code == "DGCFG_BASELINE");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "baseline": { "counts": { "DG0002": "x" } } }""").Errors,
            e => e.Code == "DGCFG_BASELINE");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "baseline": { "files": [] } }""").Errors,
            e => e.Code == "DGCFG_BASELINE");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "baseline": { "files": { "A.xaml": [] } } }""").Errors,
            e => e.Code == "DGCFG_BASELINE");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "baseline": { "files": { "A.xaml": { "DG0002": "x" } } } }""").Errors,
            e => e.Code == "DGCFG_BASELINE");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "baseline": { "files": { "A.xaml": { "DG0002": -5 } } } }""").Errors,
            e => e.Code == "DGCFG_BASELINE");
    }

    // ---------------------------------------------------------------- exemptions

    [Fact]
    public void Exemptions_Malformed_AreCollected()
    {
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "exemptions": {} }""").Errors,
            e => e.Code == "DGCFG_EXEMPTIONS");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "exemptions": [5] }""").Errors,
            e => e.Code == "DGCFG_EXEMPTIONS");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "exemptions": [ { "file": "A.xaml", "count": 1, "reason": "r" } ] }""").Errors,
            e => e.Code == "DGCFG_EXEMPTIONS");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "exemptions": [ { "rule": "DG0009", "count": 1, "reason": "r" } ] }""").Errors,
            e => e.Code == "DGCFG_EXEMPTIONS");
        Assert.Contains(
            ConfigLoader.LoadFromText("""{ "version": 1, "exemptions": [ { "rule": "DG0009", "file": "  ", "count": 1, "reason": "r" } ] }""").Errors,
            e => e.Code == "DGCFG_EXEMPTIONS");
        // Whitespace-only type is treated as untyped (no error).
        Assert.False(HasCode(
            """{ "version": 1, "exemptions": [ { "rule": "DG0009", "file": "a.xaml", "count": 1, "reason": "r", "type": "  " } ] }""",
            "DGCFG_EXEMPTIONS"));
    }

    [Fact]
    public void ValidExemption_ProducesNoErrors()
    {
        var result = ConfigLoader.LoadFromText("""
        { "version": 1, "exemptions": [ { "rule": "DG0009", "file": "App/Views/A.xaml", "count": 1, "reason": "reviewed" } ] }
        """);
        Assert.False(result.HasErrors);
    }
}
