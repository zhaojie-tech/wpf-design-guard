using System;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Xaml.Fixes;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class XamlFixEngineTests
{
    private const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static DesignGuardConfig Config() => ConfigLoader.LoadFromText("""{ "version": 1 }""").Config!;

    [Fact]
    public void DG0002_NearestLadder_TieBreaksSmaller()
    {
        var text = $"""<Grid xmlns="{P}" Margin="8,6"/>""";
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0002", Config());
        Assert.Equal(1, result.AppliedCount);
        Assert.Contains("Margin=\"8,4\"", result.NewText); // 6: |6-4|=|6-8| → smaller (4) wins
    }

    [Fact]
    public void DG0002_FixesOnlyOffendingComponents()
    {
        var text = $"""<Grid xmlns="{P}" Margin="8,6,8,4"/>""";
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0002", Config());
        Assert.Contains("Margin=\"8,4,8,4\"", result.NewText); // the two 8s untouched
    }

    [Fact]
    public void DG0002_MarkupExtension_Skipped()
    {
        var token = "{DynamicResource Spacing.Md}";
        var text = $"""<Grid xmlns="{P}" Margin="{token}"/>""";
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0002", Config());
        Assert.Equal(0, result.AppliedCount);
    }

    [Fact]
    public void DG0003_DirectionalForm_StaysOnLadderClean()
    {
        var text = $"""<Grid xmlns="{P}" Margin="8"/>""";
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0003", Config());
        Assert.Contains("Margin=\"8,8\"", result.NewText);
        // Self-check: fixing DG0003 must not introduce NEW findings (8,8 is fully on-ladder).
        Assert.Equal(1, result.AppliedCount);
        Assert.Empty(result.Unfixed);
    }

    [Fact]
    public void DG0003_SelfCheck_RollsBackWhenItWouldTripShippedRules()
    {
        // 7 is OFF the ladder: the directional form 7,7 would create two NEW DG0002 findings.
        var text = $"""<Grid xmlns="{P}" Margin="7"/>""";
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0003", Config());
        Assert.Equal(0, result.AppliedCount); // rolled back by self-check
        Assert.Contains(result.Unfixed, u => u.Contains("self-check"));
        Assert.Contains("Margin=\"7\"", result.NewText); // text unchanged
    }

    [Fact]
    public void DG0007_InsertsLineHeight_FromTokenKeyLookup()
    {
        var tokenFont = "{DynamicResource Font.Body}";
        var text = $"""<Grid xmlns="{P}"><TextBlock TextWrapping="Wrap" FontSize="{tokenFont}"/></Grid>""";
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0007", Config());
        Assert.Contains("LineHeight=\"24\"", result.NewText); // Font.Body → Body → lineHeights.Body = 24
        Assert.Contains("LineStackingStrategy=\"BlockLineHeight\"", result.NewText);
    }

    [Fact]
    public void DG0007_InsertsLineHeight_FromFontSizeValue()
    {
        var text = $"""<Grid xmlns="{P}"><TextBlock TextWrapping="Wrap" FontSize="16"/></Grid>""";
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0007", Config());
        Assert.Contains("LineHeight=\"24\"", result.NewText);
    }

    [Fact]
    public void DG0007_UnmappableFontSize_ReportsUnfixed()
    {
        var text = $"""<Grid xmlns="{P}"><TextBlock TextWrapping="Wrap"/></Grid>"""; // no FontSize at all
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0007", Config());
        Assert.Equal(0, result.AppliedCount);
        Assert.Contains(result.Unfixed, u => u.Contains("cannot infer LineHeight"));
    }

    [Fact]
    public void DG0015_InsertsModeOneWay()
    {
        var binding = "{Binding Total}";
        var text = $"""<TextBlock xmlns="{P}"><Run Text="{binding}"/></TextBlock>""";
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0015", Config());
        Assert.Contains("{Binding Total, Mode=OneWay}", result.NewText);
    }

    [Fact]
    public void DG0015_ExplicitOtherMode_NotAutoFixed()
    {
        var binding = "{Binding Total, Mode=TwoWay}";
        var text = $"""<TextBlock xmlns="{P}"><Run Text="{binding}"/></TextBlock>""";
        var result = XamlFixEngine.FixFile(text, "a.xaml", "DG0015", Config());
        Assert.Equal(0, result.AppliedCount);
        Assert.Contains(result.Unfixed, u => u.Contains("human decision"));
    }

    [Fact]
    public void UnsupportedRule_ReportsUnfixed()
    {
        var result = XamlFixEngine.FixFile("<x/>", "a.xaml", "DG0013", Config());
        Assert.Equal(0, result.AppliedCount);
        Assert.Single(result.Unfixed);
    }

    [Fact]
    public void Nearest_LadderSemantics()
    {
        var ladder = new[] { 0d, 2d, 4d, 8d, 12d, 16d, 24d, 32d };
        Assert.Equal(4, XamlFixEngine.Nearest(ladder, 6)); // tie → smaller
        Assert.Equal(8, XamlFixEngine.Nearest(ladder, 7));
        Assert.Equal(32, XamlFixEngine.Nearest(ladder, 100));
        Assert.Equal(0, XamlFixEngine.Nearest(ladder, -5));
    }
}
