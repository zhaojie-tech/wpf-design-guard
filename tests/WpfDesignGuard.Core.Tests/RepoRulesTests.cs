using System;
using System.Collections.Generic;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.RepoRules;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class RepoRuleTestBase
{
    protected const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    protected const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    protected static DesignGuardConfig Config(string? json = null) =>
        ConfigLoader.LoadFromText(json ?? """{ "version": 1 }""").Config!;

    protected static RepoEvaluation Evaluate(DesignGuardConfig config, params (string Path, string Content)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);
        return RepoRuleEngine.Evaluate(new RepoContext(map, config));
    }

    protected static string AppMerging(params string[] sources)
    {
        var merged = string.Join(string.Empty, sources.Select(s => $"""<ResourceDictionary Source="{s}"/>"""));
        return $"""
        <Application xmlns="{P}">
          <Application.Resources>
            <ResourceDictionary>
              <ResourceDictionary.MergedDictionaries>{merged}</ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
          </Application.Resources>
        </Application>
        """;
    }

    protected static string TextStyleDictionary(string body) =>
        $"""<ResourceDictionary xmlns="{P}" xmlns:x="{X}">{body}</ResourceDictionary>""";
}

public class ImplicitTextBlockStyleRuleTests : RepoRuleTestBase
{
    private const string RuleId = "DG0014";

    [Fact]
    public void ImplicitStyleMissingFontSize_IsReported()
    {
        var r = Evaluate(Config(),
            ("App.xaml", AppMerging("Themes/Text.xaml")),
            ("Themes/Text.xaml", TextStyleDictionary(
                """<Style TargetType="TextBlock"><Setter Property="Foreground" Value="Black"/></Style>""")));

        var f = Assert.Single(r.Findings, x => x.RuleId == RuleId);
        Assert.Equal("Themes/Text.xaml", f.File);
        Assert.Contains("FontSize", f.Message);
    }

    [Fact]
    public void FontSizeSetter_Passes()
    {
        var r = Evaluate(Config(),
            ("App.xaml", AppMerging("Themes/Text.xaml")),
            ("Themes/Text.xaml", TextStyleDictionary(
                """<Style TargetType="TextBlock"><Setter Property="FontSize" Value="16"/></Style>""")));
        Assert.DoesNotContain(r.Findings, x => x.RuleId == RuleId);
    }

    [Fact]
    public void BasedOnChainWithFontSize_Passes()
    {
        var r = Evaluate(Config(),
            ("App.xaml", AppMerging("Themes/Text.xaml")),
            ("Themes/Text.xaml", TextStyleDictionary(
                """
                <Style x:Key="BaseText" TargetType="TextBlock"><Setter Property="FontSize" Value="16"/></Style>
                <Style TargetType="TextBlock" BasedOn="{StaticResource BaseText}"/>
                """)));
        Assert.DoesNotContain(r.Findings, x => x.RuleId == RuleId);
    }

    [Fact]
    public void LaterDictionaryOverrides_EarlierMissingIsNotReported()
    {
        var r = Evaluate(Config(),
            ("App.xaml", AppMerging("Themes/Old.xaml", "Themes/New.xaml")),
            ("Themes/Old.xaml", TextStyleDictionary("""<Style TargetType="TextBlock"/>""")),
            ("Themes/New.xaml", TextStyleDictionary(
                """<Style TargetType="TextBlock"><Setter Property="FontSize" Value="16"/></Style>""")));
        Assert.DoesNotContain(r.Findings, x => x.RuleId == RuleId);
    }

    [Fact]
    public void LaterDictionaryWins_ReportsLaterDefinition()
    {
        var r = Evaluate(Config(),
            ("App.xaml", AppMerging("Themes/Old.xaml", "Themes/New.xaml")),
            ("Themes/Old.xaml", TextStyleDictionary(
                """<Style TargetType="TextBlock"><Setter Property="FontSize" Value="16"/></Style>""")),
            ("Themes/New.xaml", TextStyleDictionary("""<Style TargetType="TextBlock"/>""")));

        var f = Assert.Single(r.Findings, x => x.RuleId == RuleId);
        Assert.Equal("Themes/New.xaml", f.File);
    }

    [Fact]
    public void NoAppDefinition_EachDictionaryIsJudged()
    {
        var r = Evaluate(Config(),
            ("Views/Styles.xaml", TextStyleDictionary("""<Style TargetType="TextBlock"/>""")));
        Assert.Single(r.Findings, x => x.RuleId == RuleId);
    }

    [Fact]
    public void UnresolvableMergedSource_IsUnchecked()
    {
        var r = Evaluate(Config(),
            ("App.xaml", AppMerging("Themes/Missing.xaml")),
            ("Views/Page.xaml", $"""<Page xmlns="{P}"/>"""));
        Assert.Contains(r.Unchecked, u => u.Contains("Missing.xaml"));
    }

    [Fact]
    public void KeyedStyle_IsNotAnImplicitStyle()
    {
        var r = Evaluate(Config(),
            ("Views/Styles.xaml", TextStyleDictionary(
                """<Style x:Key="CaptionText" TargetType="TextBlock"/>""")));
        Assert.DoesNotContain(r.Findings, x => x.RuleId == RuleId);
    }
}

public class StyleLibraryCoverageRuleTests : RepoRuleTestBase
{
    private const string RuleId = "DG0017";

    private static DesignGuardConfig ConfigWithLibrary() =>
        Config("""{ "version": 1, "tokens": { "styleLibrary": "Themes/Controls.xaml" } }""");

    private static string Library(string body) => TextStyleDictionary(body);

    private static string View(string body) => $"""<Page xmlns="{P}">{body}</Page>""";

    [Fact]
    public void CoveredType_NoFinding()
    {
        var r = Evaluate(ConfigWithLibrary(),
            ("Views/Main.xaml", View("<Button/>")),
            ("Themes/Controls.xaml", Library(
                """<Style TargetType="Button"><Setter Property="MinHeight" Value="48"/></Style>""")));
        Assert.DoesNotContain(r.Findings, x => x.RuleId == RuleId);
    }

    [Fact]
    public void FloorTooLow_IsReported()
    {
        var r = Evaluate(ConfigWithLibrary(),
            ("Views/Main.xaml", View("<Button/>")),
            ("Themes/Controls.xaml", Library(
                """<Style TargetType="Button"><Setter Property="MinHeight" Value="40"/></Style>""")));

        var f = Assert.Single(r.Findings, x => x.RuleId == RuleId);
        Assert.Equal("Button", f.Type);
        Assert.Equal("Themes/Controls.xaml", f.File);
    }

    [Fact]
    public void MissingStyle_IsReported()
    {
        var r = Evaluate(ConfigWithLibrary(),
            ("Views/Main.xaml", View("<ToggleButton/>")),
            ("Themes/Controls.xaml", Library(
                """<Style TargetType="Button"><Setter Property="MinHeight" Value="48"/></Style>""")));

        var f = Assert.Single(r.Findings, x => x.RuleId == RuleId);
        Assert.Equal("ToggleButton", f.Type);
    }

    [Fact]
    public void TypeNotUsed_NoFinding()
    {
        var r = Evaluate(ConfigWithLibrary(),
            ("Views/Main.xaml", View("<Border/>")),
            ("Themes/Controls.xaml", Library("")));
        Assert.DoesNotContain(r.Findings, x => x.RuleId == RuleId);
    }

    [Fact]
    public void NoStyleLibraryConfigured_IsUnchecked()
    {
        var r = Evaluate(Config(),
            ("Views/Main.xaml", View("<Button/>")));
        Assert.DoesNotContain(r.Findings, x => x.RuleId == RuleId);
        Assert.Contains(r.Unchecked, u => u.Contains("DG0017"));
    }

    [Fact]
    public void LibraryPatternMatchesNothing_IsUnchecked()
    {
        var r = Evaluate(ConfigWithLibrary(),
            ("Views/Main.xaml", View("<Button/>")));
        Assert.DoesNotContain(r.Findings, x => x.RuleId == RuleId);
        Assert.Contains(r.Unchecked, u => u.Contains("no file matches"));
    }

    [Fact]
    public void MinWidthAtFloor_Covers()
    {
        var r = Evaluate(ConfigWithLibrary(),
            ("Views/Main.xaml", View("<ComboBox/>")),
            ("Themes/Controls.xaml", Library(
                """<Style TargetType="ComboBox"><Setter Property="MinWidth" Value="48"/></Style>""")));
        Assert.DoesNotContain(r.Findings, x => x.RuleId == RuleId);
    }

    [Fact]
    public void TypeScopedExemption_ConsumesTheFinding()
    {
        var config = ConfigLoader.LoadFromText("""
        {
          "version": 1,
          "tokens": { "styleLibrary": "Themes/Controls.xaml" },
          "exemptions": [
            { "rule": "DG0017", "file": "Themes/Controls.xaml", "count": 1, "reason": "template internal", "type": "Button" }
          ]
        }
        """).Config!;

        var r = Evaluate(config,
            ("Views/Main.xaml", View("<Button/>")),
            ("Themes/Controls.xaml", Library("")));
        var finding = Assert.Single(r.Findings, x => x.RuleId == RuleId);
        Assert.Equal("Button", finding.Type);

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>> { [finding.File] = new[] { finding } });
        Assert.Equal(FindingDisposition.Exempted, outcome.Findings.Single().Disposition);
    }
}
