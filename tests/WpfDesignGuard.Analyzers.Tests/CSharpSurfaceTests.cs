using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using WpfDesignGuard.Analyzers.CSharp;
using WpfDesignGuard.Analyzers.CodeFixes;
using Xunit;

namespace WpfDesignGuard.Analyzers.Tests;

/// <summary>
/// C# analyzer surfaces (DG0001/DG0005) + code-fix snapshots. Uses a direct
/// AnalyzerDriver-style host: compilation + analyzers → diagnostics, then the
/// code-fix provider → fixed source text.
/// </summary>
public class CSharpSurfaceTests
{
    private static readonly MetadataReference ObjectRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    private static ImmutableArray<Diagnostic> Analyze(string source)
    {
        var compilation = CSharpCompilation.Create("cs_surface",
            new[] { SyntaxFactory.ParseSyntaxTree(source) },
            new MetadataReference[] { ObjectRef })
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new BareFontSizeAnalyzer(), new BareColorAnalyzer()));
        return compilation.GetAnalyzerDiagnosticsAsync().Result
            .Where(d => d.Id.StartsWith("DG", StringComparison.Ordinal)).ToImmutableArray();
    }

    private static (Document Document, ImmutableArray<Diagnostic> Diagnostics) MakeDocument(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var project = workspace.AddProject(ProjectInfo.Create(
            projectId, Microsoft.CodeAnalysis.VersionStamp.Default, "fixproj", "fixproj", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(),
            metadataReferences: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }));
        var document = workspace.AddDocument(projectId, "Fix.cs", SourceText.From(source));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new BareFontSizeAnalyzer(), new BareColorAnalyzer());
        // Re-fetch through CurrentSolution: the local `project` predates AddDocument.
        var currentDocument = workspace.CurrentSolution.GetDocument(document.Id)!;
        var compilation = currentDocument.Project.GetCompilationAsync().Result!;
        var withAnalyzers = compilation.WithAnalyzers(analyzers);
        return (currentDocument, withAnalyzers.GetAnalyzerDiagnosticsAsync().Result);
    }

    private static string ApplyFix(string source, string diagnosticId)
    {
        var (document, diagnostics) = MakeDocument(source);
        Assert.True(diagnostics.Any(d => d.Id == diagnosticId),
            "expected " + diagnosticId + "; got: " + string.Join(" | ", diagnostics.Select(d => d.Id)));
        var target = diagnostics.First(d => d.Id == diagnosticId);

        var fixes = diagnosticId == "DG0001" ? (CodeFixProvider)new BareFontSizeCodeFix() : new BareColorCodeFix();
        var actions = new System.Collections.Generic.List<CodeAction>();
        var context = new CodeFixContext(document, target, (action, _) => actions.Add(action), CancellationToken.None);
        fixes.RegisterCodeFixesAsync(context).Wait();
        Assert.NotEmpty(actions);

        var operations = actions[0].GetOperationsAsync(CancellationToken.None).Result;
        var applied = Assert.IsType<ApplyChangesOperation>(operations[0]);
        var fixedRoot = applied.ChangedSolution.GetDocument(document.Id)!.GetSyntaxRootAsync().Result!;
        return fixedRoot.ToFullString();
    }

    // ------------------------------------------------------------ DG0001 C# surface

    [Fact]
    public void FontSizeLiteral_IsReported()
    {
        var d = Analyze("class C { void M() { button.FontSize = 16; } }").Single();
        Assert.Equal("DG0001", d.Id);
        Assert.Contains("16", d.GetMessage());
    }

    [Fact]
    public void ObjectInitializer_FontSize_IsReported()
    {
        var d = Assert.Single(Analyze("class C { void M() { var t = new X { FontSize = 20 }; } }"));
        Assert.Equal("DG0001", d.Id);
    }

    [Fact]
    public void TokenIndirection_Passes()
    {
        Assert.Empty(Analyze("class C { void M() { button.FontSize = FontTokens.Body; } }"));
    }

    [Fact]
    public void NonFontSizeMember_Passes()
    {
        Assert.Empty(Analyze("class C { void M() { box.Width = 16; } }"));
    }

    [Fact]
    public void NonLiteralFontSize_Passes()
    {
        Assert.Empty(Analyze("class C { void M() { button.FontSize = ComputeSize(); } }"));
    }

    [Fact]
    public void MultipleHits_AllReported()
    {
        Assert.Equal(2, Analyze("class C { void M() { a.FontSize = 16; b.FontSize = 20; } }").Length);
    }

    [Fact]
    public void FixFontName_ReplacesWithNearestTokenPlaceholder()
    {
        const string source = "class C { void M() { button.FontSize = 15; } }";
        var fixedSource = ApplyFix(source, "DG0001");
        Assert.Contains("FontSizeTokens.Caption", fixedSource); // 15 → nearest is Caption(14)
        Assert.Contains("DesignGuard: placeholder", fixedSource);
        Assert.Contains("was 15", fixedSource);
    }

    [Fact]
    public void NearestLadder_TieBreaksToSmaller()
    {
        // 19 sits between Caption(14) and Body(16)/Subtitle(18)/Title(20): nearest by |delta|
        // is Subtitle(18, delta 1) — verifies the tie/lower rule of 
        var (key, _) = TokenFixHelper.Nearest(19);
        Assert.Equal("Subtitle", key);
        var (key2, _) = TokenFixHelper.Nearest(15);
        Assert.Equal("Caption", key2); // tie 15: delta 1 to Caption(14) and Body(16) → smaller wins
    }

    // ------------------------------------------------------------ DG0005 C# surface

    [Fact]
    public void ColorsReference_IsReported()
    {
        var d = Analyze("class C { void M() { var b = Colors.Red; } }").Single();
        Assert.Equal("DG0005", d.Id);
        Assert.Contains("Colors.Red", d.GetMessage());
    }

    [Fact]
    public void ColorFromRgb_IsReported()
    {
        var d = Analyze("class C { void M() { var c = Color.FromRgb(0x33, 0x66, 0x99); } }").Single();
        Assert.Equal("DG0005", d.Id);
    }

    [Fact]
    public void TokenIndirectionColor_Passes()
    {
        Assert.Empty(Analyze("class C { void M() { var b = BrushTokens.Accent; } }"));
    }

    [Fact]
    public void UnrelatedMemberAccess_Passes()
    {
        Assert.Empty(Analyze("class C { void M() { var x = Paints.Red; } }"));
    }

    [Fact]
    public void UnrelatedInvocation_Passes()
    {
        Assert.Empty(Analyze("class C { void M() { var x = ColorHelper.FromRgb(1, 2, 3); } }"));
    }

    [Fact]
    public void MultipleColorHits_AllReported()
    {
        Assert.Equal(2, Analyze("class C { void M() { var a = Colors.Red; var b = Color.FromRgb(1,1,1); } }").Length);
    }

    [Fact]
    public void FixColor_ReplacesWithBrushTokenPlaceholder()
    {
        const string source = "class C { void M() { var b = Colors.Red; } }";
        var fixedSource = ApplyFix(source, "DG0005");
        Assert.Contains("BrushTokens.Accent", fixedSource);
        Assert.Contains("was Colors.Red", fixedSource);
    }
}

/// <summary>ADR-0005: token-dependent C# analyzer surfaces honour the config's enabled flag.</summary>
public class ConfigGateTests
{
    private static (Document Document, ImmutableArray<Diagnostic> Diagnostics) MakeDocument(
        string source, string? configJson)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var additionalTexts = configJson == null
            ? ImmutableArray<AdditionalText>.Empty
            : ImmutableArray.Create<AdditionalText>(new InMemoryText("/abs/proj/designguard.json", configJson));
        var additionalDocuments = configJson == null
            ? new List<DocumentInfo>()
            : new List<DocumentInfo>
            {
                DocumentInfo.Create(
                    DocumentId.CreateNewId(projectId),
                    "designguard.json",
                    loader: TextLoader.From(TextAndVersion.Create(
                        SourceText.From(configJson), Microsoft.CodeAnalysis.VersionStamp.Default))),
            };
        var project = workspace.AddProject(ProjectInfo.Create(
            projectId, Microsoft.CodeAnalysis.VersionStamp.Default, "gate", "gate", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(),
            metadataReferences: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            additionalDocuments: additionalDocuments));
        var document = workspace.AddDocument(projectId, "Gate.cs", SourceText.From(source));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new BareFontSizeAnalyzer());
        var compilation = workspace.CurrentSolution.GetDocument(document.Id)!.Project.GetCompilationAsync().Result!
            .WithAnalyzers(analyzers, new AnalyzerOptions(additionalTexts));
        return (workspace.CurrentSolution.GetDocument(document.Id)!, compilation.GetAnalyzerDiagnosticsAsync().Result);
    }

    private const string FontCode = "class C { void M() { button.FontSize = 16; } }";

    [Fact]
    public void ConfigDisablingRule_CSharpSurfaceStaysSilent()
    {
        var (_, diagnostics) = MakeDocument(FontCode,
            """{ "version": 1, "rules": { "DG0001": { "enabled": false } } }""");
        Assert.DoesNotContain(diagnostics, d => d.Id == "DG0001");
    }

    [Fact]
    public void ConfigEnablingRule_CSharpSurfaceReports()
    {
        var (_, diagnostics) = MakeDocument(FontCode,
            """{ "version": 1, "rules": { "DG0001": { "enabled": true } } }""");
        Assert.Contains(diagnostics, d => d.Id == "DG0001");
    }

    [Fact]
    public void NoConfig_CatalogDefaultEnabled()
    {
        var (_, diagnostics) = MakeDocument(FontCode, null);
        Assert.Contains(diagnostics, d => d.Id == "DG0001");
    }
}

public class ConfigGateDirectTests
{
    [Fact]
    public void Gate_Directly_DisabledConfig_ReturnsFalse()
    {
        var texts = ImmutableArray.Create<AdditionalText>(
            new InMemoryText("/abs/proj/designguard.json",
                """{ "version": 1, "rules": { "DG0001": { "enabled": false } } }"""));
        Assert.False(WpfDesignGuard.Analyzers.CSharp.ConfigGate.IsEnabledDirect(new AnalyzerOptions(texts), "DG0001"));
    }
}
