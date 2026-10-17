using System.Linq;
using WpfDesignGuard.Core.Xaml;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

/// <summary>v0.2.0 rolling batch: six generator-surface rules.</summary>
public class RollingRulesTests : XamlRuleTestBase
{
    protected const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    protected static string El(string inner) => $"""<Page xmlns="{P}">{inner}</Page>""";
}

public class InlineUniformSpacingRuleTests : RollingRulesTests
{
    private const string RuleId = "DG0004";

    [Fact]
    public void HorizontalStackTokenSpacing_IsReported()
    {
        var token = "{DynamicResource Spacing.Md}";
        var r = FindingsForRule(RuleId, $"""
        <Page xmlns="{P}">
          <StackPanel Orientation="Horizontal">
            <TextBox Padding="{token}"/>
          </StackPanel>
        </Page>
        """);
        var f = Assert.Single(r.Findings);
        Assert.Contains("TextBox", f.Message);
    }

    [Fact]
    public void VerticalStack_IsNotInline_NoFinding()
    {
        var token = "{DynamicResource Spacing.Md}";
        var r = FindingsForRule(RuleId, $"""
        <Page xmlns="{P}">
          <StackPanel>
            <TextBox Padding="{token}"/>
          </StackPanel>
        </Page>
        """);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void NonInputType_NoFinding()
    {
        var token = "{DynamicResource Spacing.Md}";
        var r = FindingsForRule(RuleId, $"""
        <Page xmlns="{P}">
          <StackPanel Orientation="Horizontal">
            <Border Padding="{token}"/>
          </StackPanel>
        </Page>
        """);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void UniformLiteral_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""
        <StackPanel Orientation="Horizontal"><TextBox Margin="8"/></StackPanel>
        """));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void NonUniformLiteral_NoFinding()
    {
        var r = FindingsForRule(RuleId, El("""
        <StackPanel Orientation="Horizontal"><TextBox Margin="8,4"/></StackPanel>
        """));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void TokenOutsideUniformFamily_NoFinding()
    {
        var token = "{DynamicResource Gap.Md}";
        var r = FindingsForRule(RuleId, $"""
        <Page xmlns="{P}">
          <StackPanel Orientation="Horizontal">
            <TextBox Padding="{token}"/>
          </StackPanel>
        </Page>
        """);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void FourEqualComponents_AreUniform()
    {
        var r = FindingsForRule(RuleId, El("""
        <StackPanel Orientation="Horizontal"><Button Padding="8,8,8,8"/></StackPanel>
        """));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void Disabled_ProducesNothing()
    {
        var token = "{DynamicResource Spacing.Md}";
        var r = FindingsForRule(RuleId, $"""
        <Page xmlns="{P}">
          <StackPanel Orientation="Horizontal"><TextBox Padding="{token}"/></StackPanel>
        </Page>
        """, configJson: """{ "version": 1, "rules": { "DG0004": { "enabled": false } } }""");
        Assert.Empty(r.Findings);
    }
}

public class BareDimensionRuleTests : RollingRulesTests
{
    private const string RuleId = "DG0006";

    [Fact]
    public void CornerRadiusLiteral_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Border CornerRadius="4"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void CornerRadiusToken_Passes()
    {
        var token = "{DynamicResource Radius.Sm}";
        var r = FindingsForRule(RuleId, $"""<Page xmlns="{P}"><Border CornerRadius="{token}"/></Page>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MinWidthLiteral_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Grid MinWidth="120"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void MaxWidthToken_Passes()
    {
        var token = "{DynamicResource Width.Dialog}";
        var r = FindingsForRule(RuleId, $"""<Page xmlns="{P}"><Grid MaxWidth="{token}"/></Page>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void SetterValueForm_IsReported()
    {
        var r = FindingsForRule(RuleId, $"""
        <ResourceDictionary xmlns="{P}"><Style TargetType="Border">
          <Setter Property="CornerRadius" Value="6"/>
        </Style></ResourceDictionary>
        """);
        Assert.Single(r.Findings);
    }

    [Fact]
    public void PropertyElementForm_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Border><Border.CornerRadius>6</Border.CornerRadius></Border>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""<Border CornerRadius="4" MinWidth="80"/>"""));
        Assert.Equal(2, r.Findings.Count);
    }

    [Fact]
    public void NonNumericValue_NoFinding()
    {
        var r = FindingsForRule(RuleId, El("""<Border CornerRadius="Auto"/>"""));
        Assert.Empty(r.Findings);
    }
}

public class HitTargetFloorRuleTests : RollingRulesTests
{
    private const string RuleId = "DG0008";

    [Fact]
    public void ExplicitHeightBelowFloor_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button Height="40"/>"""));
        var f = Assert.Single(r.Findings);
        Assert.Contains("40", f.Message);
    }

    [Fact]
    public void HeightAtFloor_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Button Height="48"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void WidthBelowFloor_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button Width="40"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void TokenDimension_Passes()
    {
        var token = "{DynamicResource Size.HitTarget}";
        var r = FindingsForRule(RuleId, $"""<Page xmlns="{P}"><Button Height="{token}"/></Page>""");
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void NonClickableType_NoFinding()
    {
        var r = FindingsForRule(RuleId, El("""<Border Height="10"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MinWidthBelowFloor_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<TextBox MinWidth="30"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void StyleSetter_IsNotInstanceScope_NoFinding()
    {
        // Documented blind spot: style-level floors belong to the DG0017 coverage gate.
        var r = FindingsForRule(RuleId, $"""
        <ResourceDictionary xmlns="{P}"><Style TargetType="Button">
          <Setter Property="MinHeight" Value="30"/>
        </Style></ResourceDictionary>
        """);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MultipleDimensions_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button Height="40" Width="30"/>"""));
        Assert.Equal(2, r.Findings.Count);
    }
}

public class ClickableStretchRuleTests : RollingRulesTests
{
    private const string RuleId = "DG0009";

    [Fact]
    public void StretchedButton_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button HorizontalAlignment="Stretch"/>"""));
        var f = Assert.Single(r.Findings);
        Assert.Contains("Button", f.Message);
    }

    [Fact]
    public void LeftAligned_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Button HorizontalAlignment="Left"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void ClickableTextBox_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<TextBox HorizontalAlignment="Stretch"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void NonClickableStretch_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Border HorizontalAlignment="Stretch"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void PropertyElementForm_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""
        <Button><Button.HorizontalAlignment>Stretch</Button.HorizontalAlignment></Button>
        """));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""
        <Button HorizontalAlignment="Stretch"/>
        <ToggleButton HorizontalAlignment="Stretch"/>
        """));
        Assert.Equal(2, r.Findings.Count);
    }
}

public class CloseGlyphRuleTests : RollingRulesTests
{
    private const string RuleId = "DG0011";

    [Fact]
    public void TimesGlyphContent_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button Content="&#215;"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void PlainTextContent_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Button Content="Close"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void TextGlyph_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<TextBlock Text="&#x2715;"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void BindingContent_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Button Content="{Binding CloseLabel}"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void PropertyElementForm_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button><Button.Content>&#x2716;</Button.Content></Button>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void LatinLetterX_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Button Content="X"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""
        <Button Content="&#215;"/><Button Content="&#x2715;"/>
        """));
        Assert.Equal(2, r.Findings.Count);
    }
}

public class ButtonCopyRuleTests : RollingRulesTests
{
    private const string RuleId = "DG0013";

    [Fact]
    public void ParenthesizedCopy_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button Content="Save (draft)"/>"""));
        var f = Assert.Single(r.Findings);
        Assert.Contains("parenthesis", f.Message);
    }

    [Fact]
    public void PlainCopy_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Button Content="Save"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void EllipsisChar_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button Content="More&#x2026;"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void TripleDot_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button Content="More..."/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void FullWidthParentheses_AreReported()
    {
        var r = FindingsForRule(RuleId, El("""<Button Content="保存（草稿）"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void NonButtonCopy_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<TextBlock Text="Note (draft)"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""
        <Button Content="Save (draft)"/><ToggleButton Content="More&#x2026;"/>
        """));
        Assert.Equal(2, r.Findings.Count);
    }
}
