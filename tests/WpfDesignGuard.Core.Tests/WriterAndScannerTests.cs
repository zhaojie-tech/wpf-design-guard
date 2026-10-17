using System;
using System.IO;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Xaml;
using WpfDesignGuard.Core.Xaml.Rules;
using WpfDesignGuard.Core.Workspace;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class ConfigWriterTests
{
    [Fact]
    public void RoundTrip_PreservesConfiguration()
    {
        var original = ConfigLoader.LoadFromText("""
        {
          "version": 1,
          "scope": {
            "root": "src",
            "include": ["App/**/*.xaml"],
            "exclude": ["App/Themes/**"],
            "perRule": { "DG0001": { "include": ["App/Ui/**"] } }
          },
          "rules": { "DG0002": { "severity": "warning", "enabled": false } },
          "tokens": { "hitTargetFloor": 44, "styleLibrary": "App/Themes/Controls.xaml" },
          "hardZero": { "App/Print/**": ["DG0002"] },
          "exemptions": [ { "rule": "DG0009", "file": "App/Views/X.xaml", "count": 2, "reason": "ok", "type": "RepeatButton" } ],
          "baseline": { "date": "2026-10-15", "counts": { "DG0002": 7 }, "files": { "App/Views/Y.xaml": { "DG0002": 3 } } }
        }
        """).Config!;

        var text = ConfigWriter.Write(original);
        var reloaded = ConfigLoader.LoadFromText(text);

        Assert.False(reloaded.HasErrors, string.Join(" | ", reloaded.Errors.Select(e => e.ToString())));
        var back = reloaded.Config!;

        Assert.Equal(original.Scope.Root, back.Scope.Root);
        Assert.Equal(original.Scope.Include, back.Scope.Include);
        Assert.Equal(original.Scope.Exclude, back.Scope.Exclude);
        Assert.Equal(original.Scope.PerRule["DG0001"].Include, back.Scope.PerRule["DG0001"].Include);
        Assert.False(back.Rules["DG0002"].Enabled);
        Assert.Equal(Rules.RuleSeverity.Warning, back.Rules["DG0002"].Severity);
        Assert.Equal(44, back.Tokens.HitTargetFloor);
        Assert.Equal("App/Themes/Controls.xaml", back.Tokens.StyleLibrary);
        Assert.Equal(new[] { "DG0002" }, back.HardZero["App/Print/**"]);
        Assert.Equal("RepeatButton", back.Exemptions[0].Type);
        Assert.Equal(7, back.Baseline!.Counts["DG0002"]);
        Assert.Equal(3, back.Baseline.Files["App/Views/Y.xaml"]["DG0002"]);
    }

    [Fact]
    public void RoundTrip_DefaultConfig_IsValidAndParsable()
    {
        var original = ConfigLoader.LoadFromText("""{ "version": 1 }""").Config!;
        var text = ConfigWriter.Write(original);
        var reloaded = ConfigLoader.LoadFromText(text);
        Assert.False(reloaded.HasErrors);
        Assert.Null(reloaded.Config!.Tokens.StyleLibrary);
        Assert.Null(reloaded.Config!.Baseline);
        Assert.Empty(reloaded.Config!.Exemptions);
    }

    [Fact]
    public void Write_IsDeterministic()
    {
        var config = ConfigLoader.LoadFromText("""{ "version": 1 }""").Config!;
        Assert.Equal(ConfigWriter.Write(config), ConfigWriter.Write(config));
    }

    [Fact]
    public void NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigWriter.Write(null!));
    }
}

public class WorkspaceScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dgscan-" + Guid.NewGuid().ToString("N"));

    public WorkspaceScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "App", "Views"));
        Directory.CreateDirectory(Path.Combine(_root, "App", "Themes"));
        Directory.CreateDirectory(Path.Combine(_root, "bin", "x"));
        File.WriteAllText(Path.Combine(_root, "App", "Views", "A.xaml"), "<x/>");
        File.WriteAllText(Path.Combine(_root, "App", "Views", "A.cs"), "//");
        File.WriteAllText(Path.Combine(_root, "App", "Themes", "B.xaml"), "<x/>");
        File.WriteAllText(Path.Combine(_root, "App.xaml"), "<x/>");
        File.WriteAllText(Path.Combine(_root, "bin", "x", "Junk.xaml"), "<x/>");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Scan_RespectsIncludeExclude_AndSkipsBinObj()
    {
        var scope = new ScopeConfig(
            ".",
            new[] { "App/**/*.xaml", "App.xaml" },
            new[] { "App/Themes/**" },
            new System.Collections.Generic.Dictionary<string, RuleScopeOverride>());
        var files = WorkspaceScanner.ScanXamlFiles(_root, scope);
        Assert.Equal(new[] { "App.xaml", "App/Views/A.xaml" }, files);
    }

    [Fact]
    public void Scan_MissingRoot_ReturnsEmpty()
    {
        var scope = ScopeConfig.Default;
        Assert.Empty(WorkspaceScanner.ScanXamlFiles(Path.Combine(_root, "nope"), scope));
    }

    [Fact]
    public void Scan_NullArguments_Throw()
    {
        var scope = ScopeConfig.Default;
        Assert.Throws<ArgumentNullException>(() => WorkspaceScanner.ScanXamlFiles(null!, scope));
        Assert.Throws<ArgumentNullException>(() => WorkspaceScanner.ScanXamlFiles(_root, null!));
    }
}

public class XamlIndexAndValueDirectTests
{
    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => XamlIndex.Parse(null!));
    }

    [Fact]
    public void OffsetOf_OutOfRange_Throws()
    {
        var index = XamlIndex.Parse("<a/>");
        Assert.Throws<ArgumentOutOfRangeException>(() => index.OffsetOf(99, 1));
    }

    [Fact]
    public void PositionOf_OutOfRange_Throws()
    {
        var index = XamlIndex.Parse("<a/>");
        Assert.Throws<ArgumentOutOfRangeException>(() => index.PositionOf(99));
    }

    [Fact]
    public void ValueSpanOf_NonQuotedOrMissing_FallsBackToNull()
    {
        var index = XamlIndex.Parse("""<a b="1"/>""");
        var attr = index.Document.Root!.Attribute("b")!;
        Assert.NotNull(index.ValueSpanOf(attr));

        // An attribute instance that cannot be located (synthetic) → null span.
        var synthetic = new System.Xml.Linq.XAttribute("ghost", "x");
        Assert.Null(index.ValueSpanOf(synthetic));
    }

    [Fact]
    public void PositionOf_NodeWithoutLineInfo_ReturnsNull()
    {
        var index = XamlIndex.Parse("<a><b/></a>");
        // An element from a DIFFERENT tree has no SetLineInfo annotation → null position.
        var other = System.Xml.Linq.XDocument.Parse("<a><b/></a>");
        var foreign = (System.Xml.Linq.XObject)other.Root!.Element("b")!;
        Assert.Null(index.PositionOf(foreign));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("8", false)]
    [InlineData("{DynamicResource X}", true)]
    [InlineData("{}  escaped", true)]
    [InlineData(" {Binding A}", true)]
    public void IsMarkupExtension_Matrix(string? value, bool expected)
    {
        Assert.Equal(expected, XamlValue.IsMarkupExtension(value));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("abc", false)]
    [InlineData("16.5", true)]
    [InlineData("16f", true)]
    public void IsNumber_Matrix(string? value, bool expected)
    {
        Assert.Equal(expected, XamlValue.IsNumber(value, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{StaticResource X}")]
    [InlineData("8,4,8,4,8")]
    [InlineData("auto")]
    public void TryParseThickness_NonLiterals_ReturnNull(string? value)
    {
        Assert.Null(XamlValue.TryParseThickness(value));
    }

    [Fact]
    public void XamlRule_NullDescriptor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NullDescriptorRule());
    }

    private sealed class NullDescriptorRule : XamlRule
    {
        public NullDescriptorRule()
            : base(null!)
        {
        }

        public override System.Collections.Generic.IReadOnlyList<Governance.RuleFinding> Evaluate(XamlIndex index, Configuration.DesignGuardConfig config, string file) =>
            System.Array.Empty<Governance.RuleFinding>();
    }
}
