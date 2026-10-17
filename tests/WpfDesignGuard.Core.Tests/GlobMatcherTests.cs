using WpfDesignGuard.Core.Globbing;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("**/*.xaml", "App/Views/Shell.xaml", true)]
    [InlineData("**/*.xaml", "App.xaml", true)]
    [InlineData("**/*.xaml", "App/Views/Shell.cs", false)]
    [InlineData("App/**/*.xaml", "App/Views/Shell.xaml", true)]
    [InlineData("App/**/*.xaml", "Lib/Views/Shell.xaml", false)]
    [InlineData("App/Themes/**", "App/Themes/Colors.xaml", true)]
    [InlineData("App/Themes/**", "App/Themes/Sub/Deep.xaml", true)]
    [InlineData("App/Themes/**", "App/Ui/Shell.xaml", false)]
    [InlineData("App/*.xaml", "App/Shell.xaml", true)]
    [InlineData("App/*.xaml", "App/Views/Shell.xaml", false)] // * does not cross segments
    [InlineData("App/?.xaml", "App/A.xaml", true)]
    [InlineData("App/?.xaml", "App/AB.xaml", false)]
    [InlineData("Views/ModeSwitch*.xaml", "Views/ModeSwitchDialog.xaml", true)]
    [InlineData("Views/ModeSwitch*.xaml", "Views/OtherDialog.xaml", false)]
    public void Patterns_MatchAsSpecified(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, new GlobMatcher(pattern).IsMatch(path));
    }

    [Fact]
    public void DoubleStar_MatchesZeroSegments()
    {
        Assert.True(new GlobMatcher("App/**/Shell.xaml").IsMatch("App/Shell.xaml"));
        Assert.True(new GlobMatcher("App/**/Shell.xaml").IsMatch("App/A/B/Shell.xaml"));
    }

    [Fact]
    public void Backslashes_AreNormalized()
    {
        Assert.True(new GlobMatcher("App\\**\\*.xaml").IsMatch("App/Views/S.xaml"));
        Assert.True(new GlobMatcher("App/**/*.xaml").IsMatch("App\\Views\\S.xaml"));
    }

    [Fact]
    public void Matching_IsCaseSensitiveByDefault()
    {
        Assert.False(new GlobMatcher("App/**/*.xaml").IsMatch("app/views/s.xaml"));
        Assert.True(new GlobMatcher("App/**/*.xaml", ignoreCase: true).IsMatch("app/views/s.xaml"));
    }

    [Fact]
    public void MatchesAny_ExcludeWins()
    {
        var includes = new[] { new GlobMatcher("**/*.xaml") };
        var excludes = new[] { new GlobMatcher("App/Themes/**") };
        Assert.True(GlobMatcher.MatchesAny(includes, excludes, "App/Views/S.xaml"));
        Assert.False(GlobMatcher.MatchesAny(includes, excludes, "App/Themes/Colors.xaml"));
    }

    [Fact]
    public void Wildcard_WithMultipleStars_Backtracks()
    {
        Assert.True(new GlobMatcher("a*b*c").IsMatch("aXbYc"));
        Assert.False(new GlobMatcher("a*b*c").IsMatch("aXbYd"));
        Assert.True(new GlobMatcher("Mode*Dial*1").IsMatch("ModeSwitchDialog1"));
    }

    [Fact]
    public void LiteralSegment_Mismatch_FailsFast()
    {
        Assert.False(new GlobMatcher("App/Bar.xaml").IsMatch("App/Foo.xaml"));
    }

    [Fact]
    public void StarSegment_MatchesEmptyName()
    {
        // "*" requires a non-empty segment; an empty path segment never matches it.
        Assert.False(new GlobMatcher("App/*").IsMatch("App/"));
    }

    [Fact]
    public void EmptyPattern_Throws()
    {
        Assert.Throws<ArgumentException>(() => new GlobMatcher("  "));
    }

    [Fact]
    public void NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GlobMatcher("**").IsMatch(null!));
    }
}
