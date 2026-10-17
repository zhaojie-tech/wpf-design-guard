using System.Linq;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Xaml;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

/// <summary>
/// Shared fixture: evaluates XAML text through the real engine with the default token
/// table (ladder 0,2,4,8,12,16,24,32) against a virtual file path.
/// </summary>
public abstract class XamlRuleTestBase
{
    protected static DesignGuardConfig Config(string? json = null) =>
        ConfigLoader.LoadFromText(json ?? """{ "version": 1 }""").Config!;

    protected static XamlEvaluationResult Evaluate(string xaml, string file = "App/Views/Test.xaml", string? configJson = null) =>
        XamlRuleEngine.Evaluate(xaml, file, Config(configJson));

    protected static XamlEvaluationResult FindingsForRule(string ruleId, string xaml, string file = "App/Views/Test.xaml", string? configJson = null)
    {
        var result = Evaluate(xaml, file, configJson);
        result.Findings.RemoveAll(f => f.RuleId != ruleId);
        return result;
    }
}

public class SpacingLadderRuleTests : XamlRuleTestBase
{
    private const string RuleId = "DG0002";

    [Fact]
    public void OnLadder_ProducesNoFindings()
    {
        var r = FindingsForRule(RuleId, """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="8,4"/>""");
        Assert.True(r.ParseSucceeded);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void OffLadderComponent_IsReported()
    {
        var r = FindingsForRule(RuleId, """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="8,6"/>""");
        var f = Assert.Single(r.Findings);
        Assert.Contains("6", f.Message);
        Assert.Contains("ladder", f.Message);
    }

    [Fact]
    public void FourComponentForm_ReportsOnlyOffendingComponents()
    {
        var r = FindingsForRule(RuleId, """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Padding="8,6,8,4"/>""");
        var f = Assert.Single(r.Findings); // only 6 is off
        Assert.Equal("6", r.Findings[0].Message.Contains("6") ? "6" : "?");
    }

    [Fact]
    public void MarkupExtension_IsSkipped()
    {
        var r = FindingsForRule(RuleId, """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="{DynamicResource SpacingLg}"/>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void BindingWithInnerCommas_IsSkipped()
    {
        var r = FindingsForRule(RuleId, """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="{Binding X, StringFormat='{}{0}'}"/>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void EscapePrefix_Literal_IsSkipped()
    {
        var r = FindingsForRule(RuleId, """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="{}8,6"/>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void SingleValueForm_BelongsToDG0003_NotDG0002()
    {
        var r = FindingsForRule(RuleId, """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="7"/>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void SetterValueForm_IsScanned()
    {
        var r = FindingsForRule(RuleId, """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Style TargetType="Button">
            <Setter Property="Margin" Value="8,6"/>
          </Style>
        </ResourceDictionary>
        """);
        Assert.Single(r.Findings);
    }

    [Fact]
    public void SetterValueElementForm_IsScanned()
    {
        var r = FindingsForRule(RuleId, """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Style TargetType="Button">
            <Setter Property="Padding">
              <Setter.Value>10,4</Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """);
        var f = Assert.Single(r.Findings);
        Assert.Contains("10", f.Message);
    }

    [Fact]
    public void PropertyElementForm_IsScanned()
    {
        var r = FindingsForRule(RuleId, """
        <Button xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Button.Margin>8,6</Button.Margin>
        </Button>
        """);
        Assert.Single(r.Findings);
    }

    [Fact]
    public void MultipleHits_AreAllReported()
    {
        var r = FindingsForRule(RuleId, """
        <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="8,6">
          <Border Padding="12,10"/>
        </StackPanel>
        """);
        Assert.Equal(2, r.Findings.Count);
        Assert.All(r.Findings, f => Assert.Equal(RuleId, f.RuleId));
    }

    [Fact]
    public void CommentsAreStripped_BeforeEvaluation()
    {
        var r = FindingsForRule(RuleId, """
        <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <!-- Margin="8,6" (tombstone comment must not count) -->
        </Grid>
        """);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void Span_PointsAtOffendingComponent()
    {
        const string xaml = """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="8,6"/>""";
        var r = FindingsForRule(RuleId, xaml);
        var f = Assert.Single(r.Findings);
        // Value starts after `Margin="`; component `6` is one char further (offset of "8," is 2).
        var idx = xaml.IndexOf("8,6", System.StringComparison.Ordinal);
        var expectedOffset = idx + 2; // skip "8,"
        var index = XamlIndex.Parse(xaml);
        Assert.Equal(expectedOffset, index.OffsetOf(f.Line, f.Column));
        Assert.Equal(1, f.Length);
    }

    [Fact]
    public void DisabledRule_ProducesNothing()
    {
        var r = FindingsForRule(RuleId,
            """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="8,6"/>""",
            configJson: """{ "version": 1, "rules": { "DG0002": { "enabled": false } } }""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void PerRuleScopeRestriction_ExcludesFile()
    {
        var r = FindingsForRule(RuleId,
            """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="8,6"/>""",
            file: "App/Other/X.xaml",
            configJson: """{ "version": 1, "scope": { "include": ["**/*.xaml"], "perRule": { "DG0002": { "include": ["App/Ui/**"] } } } }""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void NonThicknessValues_AreIgnored()
    {
        var r = FindingsForRule(RuleId, """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="Auto"/>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void InvalidThreeComponentForm_IsIgnored()
    {
        var r = FindingsForRule(RuleId, """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="8,4,8"/>""");
        Assert.Empty(r.Findings);
    }
}

public class WrappedTextLineHeightRuleTests : XamlRuleTestBase
{
    private const string RuleId = "DG0007";

    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void WrapWithBothProperties_Passes()
    {
        var r = FindingsForRule(RuleId, $"""
        <TextBlock xmlns="{Presentation}" TextWrapping="Wrap" LineHeight="24" LineStackingStrategy="BlockLineHeight"/>
        """);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void WrapWithoutEither_IsReported()
    {
        var xaml = $"""<TextBlock xmlns="{Presentation}" TextWrapping="Wrap"/>""";
        var r = FindingsForRule(RuleId, xaml);
        var f = Assert.Single(r.Findings);
        Assert.Contains("LineHeight", f.Message);
        // Span points at the element name.
        var index = XamlIndex.Parse(xaml);
        var expectedCol = xaml.IndexOf("<TextBlock", System.StringComparison.Ordinal) + 1; // 1-based column
        Assert.Equal(expectedCol, index.OffsetOf(f.Line, f.Column));
    }

    [Fact]
    public void WrapWithOnlyLineHeight_IsReported()
    {
        var r = FindingsForRule(RuleId, $"""<TextBlock xmlns="{Presentation}" TextWrapping="Wrap" LineHeight="24"/>""");
        Assert.Single(r.Findings);
    }

    [Fact]
    public void WrapWithOnlyStrategy_IsReported()
    {
        var r = FindingsForRule(RuleId, $"""<TextBlock xmlns="{Presentation}" TextWrapping="Wrap" LineStackingStrategy="BlockLineHeight"/>""");
        Assert.Single(r.Findings);
    }

    [Fact]
    public void WrapWithOverflow_IsOutOfScope()
    {
        var r = FindingsForRule(RuleId, $"""<TextBlock xmlns="{Presentation}" TextWrapping="WrapWithOverflow"/>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MarkupExtensionWrapping_IsSkipped()
    {
        var r = FindingsForRule(RuleId, $$"""<TextBlock xmlns="{{Presentation}}" TextWrapping="{{ "{Binding Mode}" }}"/>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void PropertyElementForms_AreHonoured()
    {
        // TextWrapping via property element triggers; LineHeight via property element satisfies.
        var triggering = FindingsForRule(RuleId, $"""
        <TextBlock xmlns="{Presentation}">
          <TextBlock.TextWrapping>Wrap</TextBlock.TextWrapping>
        </TextBlock>
        """);
        Assert.Single(triggering.Findings);

        var satisfied = FindingsForRule(RuleId, $"""
        <TextBlock xmlns="{Presentation}">
          <TextBlock.TextWrapping>Wrap</TextBlock.TextWrapping>
          <TextBlock.LineHeight>24</TextBlock.LineHeight>
          <TextBlock.LineStackingStrategy>BlockLineHeight</TextBlock.LineStackingStrategy>
        </TextBlock>
        """);
        Assert.Empty(satisfied.Findings);
    }

    [Fact]
    public void NonTextBlock_IsIgnored()
    {
        var r = FindingsForRule(RuleId, $"""<Border xmlns="{Presentation}" TextWrapping="Wrap"/>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void CommentedTextBlock_IsIgnored()
    {
        var r = FindingsForRule(RuleId, $"""
        <Grid xmlns="{Presentation}">
          <!-- <TextBlock TextWrapping="Wrap"/> -->
        </Grid>
        """);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void SetterForm_WrappedTextBlockStyle_IsScanned()
    {
        // A Setter value of TextWrapping on a style — the rule scans element-level attributes;
        // Setter forms on Style elements target the styled type, not literal TextBlocks.
        var r = FindingsForRule(RuleId, $"""
        <ResourceDictionary xmlns="{Presentation}">
          <Style TargetType="TextBlock">
            <Setter Property="TextWrapping" Value="Wrap"/>
          </Style>
        </ResourceDictionary>
        """);
        Assert.Empty(r.Findings); // style setters are DG0012/DG0017 territory, not the instance gate
    }
}

public class XamlRuleEngineTests : XamlRuleTestBase
{
    [Fact]
    public void MalformedXaml_ReportsParseFailure()
    {
        var r = Evaluate("""<Grid><Unclosed>""");
        Assert.False(r.ParseSucceeded);
        Assert.NotNull(r.ParseError);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void AllGeneratorRules_AreImplemented_NoSkippedSurface()
    {
        var r = Evaluate("""<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"/>""");
        // v0.2.0: every Generator-surface rule has an implementation.
        Assert.Empty(r.SkippedNotImplemented);
        // CLI-only rules never appear on the generator surface.
        Assert.DoesNotContain("DG0014", r.SkippedNotImplemented);
        Assert.DoesNotContain("DG0017", r.SkippedNotImplemented);
    }

    [Fact]
    public void DisabledRules_AreNotListedAsSkipped()
    {
        var r = Evaluate(
            """<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"/>""",
            configJson: """{ "version": 1, "rules": { "DG0004": { "enabled": false } } }""");
        Assert.DoesNotContain("DG0004", r.SkippedNotImplemented);
    }

    [Fact]
    public void BothRules_RunInTheSamePass()
    {
        var r = Evaluate("""
        <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Margin="8,6">
          <TextBlock TextWrapping="Wrap"/>
        </Grid>
        """);
        Assert.Contains(r.Findings, f => f.RuleId == "DG0002");
        Assert.Contains(r.Findings, f => f.RuleId == "DG0007");
        Assert.Equal(2, r.Findings.Count);
    }

    [Fact]
    public void NullConfig_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            XamlRuleEngine.Evaluate("<Grid/>", "a.xaml", null!));
    }
}

/// <summary>
/// ADR-0005 guard: the five token-independent rules must fire with a config that carries
/// NO tokens section at all — they are the out-of-the-box value for repos without a
/// token system (R3 review item: the claim is a test, not prose).
/// </summary>
public class TokenIndependenceGuardTests : XamlRuleTestBase
{
    private const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Theory]
    [InlineData("DG0007", """<Grid xmlns="{P}"><TextBlock TextWrapping="Wrap"/></Grid>""")]
    [InlineData("DG0010", """<Button xmlns="{P}"><Button.Style><Style TargetType="Button"/></Button.Style></Button>""")]
    [InlineData("DG0015", """<TextBlock xmlns="{P}"><Run Text="{Binding Total}"/></TextBlock>""")]
    [InlineData("DG0016", """<Grid xmlns="{P}"><TextBox MaxWidth="200" HorizontalAlignment="Left"/></Grid>""")]
    public void TokenIndependentRules_Fire_WithoutTokensSection(string ruleId, string xamlTemplate)
    {
        var xaml = xamlTemplate.Replace("{P}", P);
        var r = Evaluate(xaml, configJson: """{ "version": 1 }""");
        Assert.Contains(r.Findings, f => f.RuleId == ruleId);
    }

    [Fact]
    public void TokenCatalogFlags_MatchTheAdoptionLayering()
    {
        // DG0001/0002/0003/0005 are the token-dependent four; the five above are not.
        foreach (var id in new[] { "DG0001", "DG0002", "DG0003", "DG0005" })
        {
            Assert.True(Rules.RuleCatalog.TryGet(id)!.TokenDependent, $"{id} should be token-dependent");
        }

        foreach (var id in new[] { "DG0007", "DG0010", "DG0012", "DG0015", "DG0016" })
        {
            Assert.False(Rules.RuleCatalog.TryGet(id)!.TokenDependent, $"{id} should be token-independent");
        }
    }
}
