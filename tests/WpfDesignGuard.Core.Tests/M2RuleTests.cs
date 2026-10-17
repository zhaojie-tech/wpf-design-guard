using System.Linq;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Xaml;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

/// <summary>M2 rule batch: DG0001/0003/0005/0010/0015/0016 generator-surface tests.</summary>
public class M2RuleTests : XamlRuleTestBase
{
    protected const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    protected static string El(string inner) => $"""<Page xmlns="{P}">{inner}</Page>""";
}

public class BareFontSizeRuleTests : M2RuleTests
{
    private const string RuleId = "DG0001";

    [Fact]
    public void TokenReference_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<TextBlock FontSize="{DynamicResource Font.Body}"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void BareLiteral_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<TextBlock FontSize="16"/>"""));
        var f = Assert.Single(r.Findings);
        Assert.Contains("16", f.Message);
    }

    [Fact]
    public void AttachedPropertyForm_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Grid TextBlock.FontSize="20"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void SetterForm_IsReported()
    {
        var r = FindingsForRule(RuleId, $"""
        <ResourceDictionary xmlns="{P}"><Style TargetType="TextBlock">
          <Setter Property="FontSize" Value="18"/>
        </Style></ResourceDictionary>
        """);
        Assert.Single(r.Findings);
    }

    [Fact]
    public void PropertyElementForm_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<TextBlock><TextBlock.FontSize>16</TextBlock.FontSize></TextBlock>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void NonNumericValue_IsIgnored()
    {
        var r = FindingsForRule(RuleId, El("""<TextBlock FontSize="{Binding VM.Size}"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""<TextBlock FontSize="16"/><TextBlock FontSize="20"/>"""));
        Assert.Equal(2, r.Findings.Count);
    }

    [Fact]
    public void Disabled_ProducesNothing()
    {
        var r = FindingsForRule(RuleId, El("""<TextBlock FontSize="16"/>"""),
            configJson: """{ "version": 1, "rules": { "DG0001": { "enabled": false } } }""");
        Assert.Empty(r.Findings);
    }
}

public class UniformSpacingLiteralRuleTests : M2RuleTests
{
    private const string RuleId = "DG0003";

    [Fact]
    public void SingleValueLiteral_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Grid Margin="8"/>"""));
        var f = Assert.Single(r.Findings);
        Assert.Contains("Margin", f.Message);
    }

    [Fact]
    public void TokenReference_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Grid Margin="{DynamicResource Spacing.Md}"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MultiComponent_BelongsToDG0002_NotHere()
    {
        var r = FindingsForRule(RuleId, El("""<Grid Margin="8,4"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void PaddingForm_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Border Padding="12"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void SetterForm_IsReported()
    {
        var r = FindingsForRule(RuleId, $"""
        <ResourceDictionary xmlns="{P}"><Style TargetType="Button">
          <Setter Property="Padding" Value="10"/>
        </Style></ResourceDictionary>
        """);
        Assert.Single(r.Findings);
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""<Grid Margin="8"/><Border Padding="4"/>"""));
        Assert.Equal(2, r.Findings.Count);
    }

    [Fact]
    public void Zero_IsStillReported()
    {
        // 0 is on the ladder but the single-value form is still the DG0003 surface.
        var r = FindingsForRule(RuleId, El("""<Grid Margin="0"/>"""));
        Assert.Single(r.Findings);
    }
}

public class BareColorRuleTests : M2RuleTests
{
    private const string RuleId = "DG0005";

    [Fact]
    public void HexLiteral_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Rectangle Fill="#FF336699"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void ShortHex_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Rectangle Fill="#F00"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void TokenReference_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Rectangle Fill="{DynamicResource Brush.Accent}"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void NonColorString_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<TextBlock Text="#notice"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void PropertyElementForm_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Rectangle><Rectangle.Fill>#FF336699</Rectangle.Fill></Rectangle>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void SetterValueForm_IsReported()
    {
        var r = FindingsForRule(RuleId, $"""
        <ResourceDictionary xmlns="{P}"><Style TargetType="Border">
          <Setter Property="Background"><Setter.Value>#FF336699</Setter.Value></Setter>
        </Style></ResourceDictionary>
        """);
        Assert.Single(r.Findings);
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""<Rectangle Fill="#FFF" Stroke="#000"/>"""));
        Assert.Equal(2, r.Findings.Count);
    }
}

public class InlineStyleBasedOnRuleTests : M2RuleTests
{
    private const string RuleId = "DG0010";

    [Fact]
    public void InlineStyleWithoutBasedOn_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""
        <Button>
          <Button.Style><Style TargetType="Button"/></Button.Style>
        </Button>
        """));
        var f = Assert.Single(r.Findings);
        Assert.Contains("Button", f.Message);
    }

    [Fact]
    public void InlineStyleWithBasedOn_Passes()
    {
        var basedOn = "{StaticResource {x:Type Button}}";
        var r = FindingsForRule(RuleId, $"""
        <Page xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Button>
            <Button.Style><Style TargetType="Button" BasedOn="{basedOn}"/></Button.Style>
          </Button>
        </Page>
        """);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void StyleLibraryDefinition_IsNotACallSite()
    {
        var r = FindingsForRule(RuleId, $"""
        <ResourceDictionary xmlns="{P}" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button" x:Key="PrimaryButton"/>
        </ResourceDictionary>
        """);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MultipleInlineStyles_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""
        <Button><Button.Style><Style TargetType="Button"/></Button.Style></Button>
        <ToggleButton><ToggleButton.Style><Style TargetType="ToggleButton"/></ToggleButton.Style></ToggleButton>
        """));
        Assert.Equal(2, r.Findings.Count);
    }

    [Fact]
    public void NonStylePropertyElement_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Button><Button.Template><ControlTemplate/></Button.Template></Button>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void Disabled_ProducesNothing()
    {
        var r = FindingsForRule(RuleId, El("""<Button><Button.Style><Style TargetType="Button"/></Button.Style></Button>"""),
            configJson: """{ "version": 1, "rules": { "DG0010": { "enabled": false } } }""");
        Assert.Empty(r.Findings);
    }
}

public class RunTextBindingRuleTests : M2RuleTests
{
    private const string RuleId = "DG0015";

    [Fact]
    public void BindingWithoutMode_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Run Text="{Binding Total}"/>"""));
        var f = Assert.Single(r.Findings);
        Assert.Contains("OneWay", f.Message);
    }

    [Fact]
    public void BindingWithOneWay_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Run Text="{Binding Total, Mode=OneWay}"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void ExplicitTwoWay_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Run Text="{Binding Total, Mode=TwoWay}"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void BareBindingToken_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<Run Text="{Binding}"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void LiteralText_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Run Text="hello"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void NonBindingExtension_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Run Text="{DynamicResource Copy.Close}"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""<Run Text="{Binding A}"/><Run Text="{Binding B}"/>"""));
        Assert.Equal(2, r.Findings.Count);
    }

    [Fact]
    public void ModeMatching_IsCaseInsensitive()
    {
        var r = FindingsForRule(RuleId, El("""<Run Text="{Binding Total, mode=oneway}"/>"""));
        Assert.Empty(r.Findings);
    }
}

public class MaxWidthCollapseRuleTests : M2RuleTests
{
    private const string RuleId = "DG0016";

    [Fact]
    public void MaxWidthWithLeft_IsReported()
    {
        var r = FindingsForRule(RuleId, El("""<TextBox MaxWidth="200" HorizontalAlignment="Left"/>"""));
        var f = Assert.Single(r.Findings);
        Assert.Contains("TextBox", f.Message);
    }

    [Fact]
    public void MaxWidthWithStretch_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<TextBox MaxWidth="200" HorizontalAlignment="Stretch"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void TokenMaxWidth_StillReported()
    {
        // The combination is the anti-pattern, regardless of where the width comes from.
        var r = FindingsForRule(RuleId, El("""<ComboBox MaxWidth="{DynamicResource Width.Input}" HorizontalAlignment="Left"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void NonInputType_Passes()
    {
        var r = FindingsForRule(RuleId, El("""<Border MaxWidth="200" HorizontalAlignment="Left"/>"""));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void AlignmentCaseInsensitive()
    {
        var r = FindingsForRule(RuleId, El("""<Button MaxWidth="100" HorizontalAlignment="left"/>"""));
        Assert.Single(r.Findings);
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        var r = FindingsForRule(RuleId, El("""
        <TextBox MaxWidth="200" HorizontalAlignment="Left"/>
        <DatePicker MaxWidth="180" HorizontalAlignment="Left"/>
        """));
        Assert.Equal(2, r.Findings.Count);
    }

    [Fact]
    public void ConfigurableInputTypes_AreHonoured()
    {
        var r = FindingsForRule(RuleId, El("""<TextBox MaxWidth="200" HorizontalAlignment="Left"/>"""),
            configJson: """{ "version": 1, "tokens": { "inlineInputTypes": ["ComboBox"] } }""");
        Assert.Empty(r.Findings); // TextBox no longer an input type after whole-table replacement
    }
}
