using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WpfDesignGuard.Analyzers;
using WpfDesignGuard.Core.Xaml;
using Xunit;

namespace WpfDesignGuard.Analyzers.Tests;

/// <summary>
/// DG0012 symbol-resolution tests against an in-memory mock "WPF" assembly that
/// advertises the presentation namespace via XmlnsDefinitionAttribute (clean-room:
/// the attribute class is re-declared, no WPF reference needed on the test host).
/// </summary>
public class SetterExistenceTests
{
    internal const string MockSource = """
    using System;
    [assembly: System.Windows.Markup.XmlnsDefinition("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "Wpf.Mock")]
    namespace System.Windows.Markup
    {
        [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
        public class XmlnsDefinitionAttribute : Attribute
        {
            public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
        }
    }
    namespace Wpf.Mock
    {
        public class MockButton
        {
            public int Padding { get; set; }
            public static readonly object PaddingProperty = new object();
        }
        public class MockToggleButton : MockButton { }
        public class MockSpecial
        {
            public static readonly object MockSpecialFlagProperty = new object();
        }
    }
    """;

    private static (Compilation Compilation, MetadataReference MockReference) BuildCompilation()
    {
        var objectRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var mock = CSharpCompilation.Create(
            "MockWpf",
            new[] { SyntaxFactory.ParseSyntaxTree(MockSource) },
            new[] { objectRef });
        var mockRef = mock.ToMetadataReference();

        var main = CSharpCompilation.Create(
            "Main",
            new[] { SyntaxFactory.ParseSyntaxTree("class C { }") },
            new MetadataReference[] { objectRef, mockRef });
        return (main, mockRef);
    }

    private static SetterSpec Spec(string typeName = "MockButton", string typePrefix = "",
        string property = "Padding", string propertyPrefix = "", bool attached = false,
        string ownerXmlnsPrefix = "") =>
        new(typeName, typePrefix, property, propertyPrefix, attached, ownerXmlnsPrefix, 1, 1, 1);

    private static SetterExistenceChecker Checker(Compilation c, SymbolQueryTelemetry t) =>
        new(c, new System.Collections.Generic.Dictionary<string, string>(), t);

    [Fact]
    public void ExistingClrProperty_Exists()
    {
        var (c, _) = BuildCompilation();
        Assert.Equal(SetterCheckResult.Exists, Checker(c, new SymbolQueryTelemetry()).Check(Spec()));
    }

    [Fact]
    public void InheritedProperty_Exists()
    {
        var (c, _) = BuildCompilation();
        Assert.Equal(SetterCheckResult.Exists,
            Checker(c, new SymbolQueryTelemetry()).Check(Spec(typeName: "MockToggleButton")));
    }

    [Fact]
    public void MissingProperty_IsMissing()
    {
        var (c, _) = BuildCompilation();
        Assert.Equal(SetterCheckResult.Missing,
            Checker(c, new SymbolQueryTelemetry()).Check(Spec(property: "Nope")));
    }

    [Fact]
    public void AttachedProperty_DependencyField_Exists()
    {
        var (c, _) = BuildCompilation();
        Assert.Equal(SetterCheckResult.Exists,
            Checker(c, new SymbolQueryTelemetry()).Check(
                Spec(property: "MockSpecialFlag", propertyPrefix: "MockSpecial", attached: true)));
    }

    [Fact]
    public void AttachedProperty_MissingField_IsMissing()
    {
        var (c, _) = BuildCompilation();
        Assert.Equal(SetterCheckResult.Missing,
            Checker(c, new SymbolQueryTelemetry()).Check(
                Spec(property: "Ghost", propertyPrefix: "MockSpecial", attached: true)));
    }

    [Fact]
    public void UnknownType_IsUnchecked_NeverFalsePositive()
    {
        var (c, _) = BuildCompilation();
        Assert.Equal(SetterCheckResult.Unchecked,
            Checker(c, new SymbolQueryTelemetry()).Check(Spec(typeName: "DoesNotExist")));
    }

    [Fact]
    public void UnmappedPrefix_IsUnchecked()
    {
        var (c, _) = BuildCompilation();
        Assert.Equal(SetterCheckResult.Unchecked,
            Checker(c, new SymbolQueryTelemetry()).Check(Spec(typePrefix: "nope")));
    }

    [Fact]
    public void Memo_RepeatedTypeLookup_CountsOnce()
    {
        var (c, _) = BuildCompilation();
        var telemetry = new SymbolQueryTelemetry();
        var checker = Checker(c, telemetry);

        checker.Check(Spec());                          // first lookup: MockButton via presentation ns
        checker.Check(Spec());                          // same spec: memo hit
        checker.Check(Spec(property: "Padding2"));      // same type, new property: type memo hit

        Assert.Equal(1, telemetry.Current); // exactly one type lookup for MockButton (may include base ns scans)
        Assert.True(telemetry.Current <= 2, $"expected memoized lookups, got {telemetry.Current}");
    }

    [Fact]
    public void ParseClrNamespaceDeclarations_ExtractsClrNamespaces()
    {
        var map = SetterExistenceChecker.ParseClrNamespaceDeclarations(
            """<Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:local="clr-namespace:App.Controls;assembly=App" xmlns:sys="clr-namespace:System;assembly=mscorlib"/>""");
        Assert.Equal("App.Controls", map["local"]);
        Assert.Equal("System", map["sys"]);
    }

    [Fact]
    public void GeneratorPipeline_ReportsMissingSetter()
    {
        var (compilation, _) = BuildCompilation();
        var generator = new XamlDesignGenerator();

        const string config = """{ "version": 1 }""";
        const string xaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Style TargetType="MockButton">
            <Setter Property="Nope"/>
          </Style>
        </ResourceDictionary>
        """;

        var objectRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var withMock = CSharpCompilation.Create("MainWithMock",
            new[] { SyntaxFactory.ParseSyntaxTree("class C { }") },
            new[] { objectRef, BuildCompilation().MockReference });

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            System.Collections.Immutable.ImmutableArray.CreateRange(new AdditionalText[]
            {
                new InMemoryText("/abs/proj/designguard.json", config),
                new InMemoryText("/abs/proj/Ui/Styles.xaml", xaml),
            }),
            new CSharpParseOptions());
        driver = driver.RunGenerators(withMock);

        var diagnostics = driver.GetRunResult().Diagnostics;
        var compDiags = withMock.GetDiagnostics().Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).ToList();
        Assert.True(diagnostics.Any(d => d.Id == "DG0012"),
            "comp warnings: " + string.Join(" | ", compDiags.Select(d => d.Id + ":" + d.GetMessage().Substring(0, Math.Min(100, d.GetMessage().Length)))) +
            "no DG0012; all: " + string.Join(" | ", diagnostics.Select(d => d.Id + ":" + d.GetMessage().Substring(0, Math.Min(80, d.GetMessage().Length)))));
        var dg0012 = diagnostics.FirstOrDefault(d => d.Id == "DG0012");
        Assert.NotNull(dg0012);
        Assert.Contains("Nope", dg0012.GetMessage());
        Assert.Contains("MockButton", dg0012.GetMessage());
        // span promise: clickable diagnostics point into the XAML file.
        var span = dg0012.Location.GetLineSpan();
        Assert.Equal("/abs/proj/Ui/Styles.xaml", span.Path);
        Assert.Equal(3, span.StartLinePosition.Line + 1); // <Setter Property="Nope"/> line (raw string starts at line 1)
    }
}

public class MockAssemblyProbe
{
    [Fact]
    public void Probe_XmlnsDefinitionAttribute_Visible()
    {
        var objectRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var mock = CSharpCompilation.Create(
            "MockWpfProbe",
            new[] { SyntaxFactory.ParseSyntaxTree(SetterExistenceTests.MockSource) },
            new[] { objectRef });
        var main = CSharpCompilation.Create("ProbeMain",
            new[] { SyntaxFactory.ParseSyntaxTree("class C { }") },
            new MetadataReference[] { objectRef, mock.ToMetadataReference() });

        var found = new System.Collections.Generic.List<string>();
        foreach (var reference in main.References)
        {
            if (main.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm)
            {
                foreach (var attr in asm.GetAttributes())
                {
                    var display = attr.AttributeClass?.ToDisplayString();
                    found.Add(display + " args=[" + string.Join(",", attr.ConstructorArguments.Select(a => a.Value)) + "]");
                }
            }
        }

        var xmlns = found.Where(f => f.Contains("XmlnsDefinition")).ToList();
        var mockErrors = mock.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.True(xmlns.Count > 0,
            "no XmlnsDefinitionAttribute; mock errors: " + string.Join(" | ", mockErrors.Select(e => e.ToString())) +
            "; all attrs: " + string.Join(" | ", found.Take(8)));
    }
}

/// <summary>Corpus self-bootstrap for the DG0012 symbol layer (samples/DesignGuardCorpus).</summary>
public class CorpusSymbolBootstrapTests
{
    [Fact]
    public void CorpusDG0012_PositiveFires_NegativeSilent()
    {
        var corpusRoot = FindCorpus();
        var objectRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

        // The corpus project itself is the referenced "user assembly" surface:
        // SampleControl (PageSize + PageProperty) lives in DesignGuardCorpus.Controls.
        var controlsSource = System.IO.File.ReadAllText(System.IO.Path.Combine(corpusRoot, "Controls", "SampleControl.cs"));
        var corpusAssembly = CSharpCompilation.Create("DesignGuardCorpus",
            new[] { SyntaxFactory.ParseSyntaxTree(controlsSource) },
            new MetadataReference[] { objectRef });

        var compilation = CSharpCompilation.Create("CorpusMain",
            new[] { SyntaxFactory.ParseSyntaxTree("class C { }") },
            new MetadataReference[] { objectRef, corpusAssembly.ToMetadataReference() });

        var telemetry = new SymbolQueryTelemetry();
        var positive = CheckFile(compilation, telemetry, System.IO.Path.Combine(corpusRoot, "Xaml", "DG0012-positive.xaml"));
        var negative = CheckFile(compilation, telemetry, System.IO.Path.Combine(corpusRoot, "Xaml", "DG0012-negative.xaml"));

        Assert.Equal(SetterCheckResult.Missing, Assert.Single(positive));
        Assert.Equal(SetterCheckResult.Exists, Assert.Single(negative));
    }

    private static string FindCorpus()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "samples", "DesignGuardCorpus");
            if (System.IO.Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent!;
        }

        throw new InvalidOperationException("corpus not found");
    }

    private static System.Collections.Generic.IReadOnlyList<SetterCheckResult> CheckFile(
        Compilation compilation, SymbolQueryTelemetry telemetry, string path)
    {
        var text = System.IO.File.ReadAllText(path);
        var index = WpfDesignGuard.Core.Xaml.XamlIndex.Parse(text);
        var specs = WpfDesignGuard.Core.Xaml.SetterSpecCollector.Collect(index);
        var namespaces = SetterExistenceChecker.ParseClrNamespaceDeclarations(text);
        var checker = new SetterExistenceChecker(compilation, namespaces, telemetry);
        return specs.Select(checker.Check).ToList();
    }
}
