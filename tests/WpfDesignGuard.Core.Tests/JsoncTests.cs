using System;
using WpfDesignGuard.Core;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class JsoncTests
{
    [Fact]
    public void PlainJson_PassesThroughUnchanged()
    {
        const string json = """{"a":1,"b":[2,3]}""";
        Assert.Equal(json, Jsonc.Strip(json));
    }

    [Fact]
    public void LineComment_IsStripped_SpacePreserved()
    {
        var result = Jsonc.Strip("{\n  // comment\n  \"a\": 1\n}");
        Assert.Contains("\"a\": 1", result);
        Assert.DoesNotContain("comment", result);
        // Newlines survive so JSON error positions stay truthful.
        Assert.Equal(3, result.Count(c => c == '\n'));
    }

    [Fact]
    public void BlockComment_IsStripped()
    {
        var result = Jsonc.Strip("{ /* multi\nline */ \"a\": 1 }");
        Assert.Contains("\"a\": 1", result);
        Assert.DoesNotContain("multi", result);
        Assert.Contains("\n", result); // inner newline preserved
    }

    [Fact]
    public void CommentInsideString_IsKept()
    {
        var result = Jsonc.Strip("""{"url": "http://x/*y*/z"}""");
        Assert.Contains("http://x/*y*/z", result);
    }

    [Fact]
    public void EscapedQuoteInsideString_DoesNotEndString()
    {
        var result = Jsonc.Strip("""{"a": "say \"// hi\"", "b": 1}""");
        Assert.Contains("say \\\"// hi\\\"", result);
        Assert.Contains("\"b\": 1", result);
    }

    [Fact]
    public void TrailingCommaInObject_IsStripped()
    {
        var result = Jsonc.Strip("""{"a": 1, "b": 2,}""");
        Assert.Equal("""{"a": 1, "b": 2 }""", result);
    }

    [Fact]
    public void TrailingCommaInArray_IsStripped()
    {
        var result = Jsonc.Strip("[1, 2, 3,\n]");
        Assert.Equal("[1, 2, 3 \n]", result);
    }

    [Fact]
    public void CommaInsideString_BeforeBracket_IsKept()
    {
        var result = Jsonc.Strip("""{"a": "x,]" }""");
        Assert.Contains("x,]", result);
    }

    [Fact]
    public void BlockComment_ContainingNewline_PreservesIt()
    {
        var result = Jsonc.Strip("{ /* a\nb */ \"k\": 1 }");
        Assert.Contains("\n", result);
        Assert.DoesNotContain("a\nb", result.Replace("\n", ""));
    }

    [Fact]
    public void LineComment_AtEndOfInput_DoesNotHang()
    {
        var result = Jsonc.Strip("{\"a\": 1} // trailing");
        Assert.DoesNotContain("trailing", result);
    }

    [Fact]
    public void UnclosedBlockComment_DoesNotHang()
    {
        var result = Jsonc.Strip("{ /* dangling");
        Assert.DoesNotContain("dangling", result);
    }

    [Fact]
    public void Strip_Output_IsParseableJson()
    {
        var jsonc = """
        {
          // scope
          "version": 1,
          /* tokens
             calibrated */
          "tokens": { "spacingLadder": [0, 4, 8,], },
        }
        """;
        var stripped = Jsonc.Strip(jsonc);
        var doc = System.Text.Json.JsonDocument.Parse(stripped);
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Jsonc.Strip(null!));
    }
}
