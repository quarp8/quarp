using System.Text;
using Xunit;

namespace Quarp.CartKit.Tests;

/// <summary>
/// The 256 KB code limit (SPEC-8 §6): comments are free, line endings are normalized,
/// and the cap itself is enforced through <see cref="CodeBudget.Validate"/>.
/// </summary>
public class CodeBudgetTests
{
    private static List<CartSourceFile> Sources(params string[] texts)
    {
        var list = new List<CartSourceFile>(texts.Length);
        for (int i = 0; i < texts.Length; i++)
        {
            list.Add(new CartSourceFile($"src/file{i}.cs", texts[i]));
        }
        return list;
    }

    [Fact]
    public void CommentsCostNothing()
    {
        const string bare = "class C{int f;}";
        const string commented = "class C{/* a comment inside */int f;}";
        Assert.Equal(CodeBudget.Measure(Sources(bare)), CodeBudget.Measure(Sources(commented)));
    }

    [Fact]
    public void AllCommentKindsAreFree()
    {
        const string bare = "class C\n{\n    int f;\n}\n";
        const string commented =
            "// line comment\n"
            + "/* block\n   comment */\n"
            + "/// <summary>doc comment</summary>\n"
            + "class C// trailing\n{\n    int f;/* inline */\n}\n";
        // The commented version measures as the bare code plus a few leftover newlines —
        // 83 bytes of comment text must not count.
        int bareBytes = CodeBudget.Measure(Sources(bare));
        int commentedBytes = CodeBudget.Measure(Sources(commented));
        Assert.True(commentedBytes <= bareBytes + 8,
            $"comments leaked into the budget: bare {bareBytes}, commented {commentedBytes}");
    }

    [Fact]
    public void HugeCommentsDoNotBreachTheLimit()
    {
        var comment = new StringBuilder("/*");
        comment.Append('x', 200_000);           // 200 KB of comment, way over the cap by itself
        comment.Append("*/");
        string source = comment + "\nclass C{int f;}";
        CodeBudget.Validate(Sources(source));   // must not throw
        Assert.True(CodeBudget.Measure(Sources(source)) < 100);
    }

    [Fact]
    public void CrLfNormalizesToLf()
    {
        const string lf = "class C\n{\n    int f;\n}\n";
        string crlf = lf.Replace("\n", "\r\n");
        Assert.Equal(CodeBudget.Measure(Sources(lf)), CodeBudget.Measure(Sources(crlf)));
    }

    [Fact]
    public void MeasureCountsUtf8BytesNotChars()
    {
        // 'Ж' is 2 bytes in UTF-8; the budget counts bytes.
        const string ascii = "class C{string s=\"aa\";}";
        const string cyrillic = "class C{string s=\"ЖЖ\";}";
        Assert.Equal(CodeBudget.Measure(Sources(ascii)) + 2, CodeBudget.Measure(Sources(cyrillic)));
    }

    [Fact]
    public void MeasureSumsAllSources()
    {
        const string a = "class A{}";
        const string b = "class B{int f;}";
        Assert.Equal(
            CodeBudget.Measure(Sources(a)) + CodeBudget.Measure(Sources(b)),
            CodeBudget.Measure(Sources(a, b)));
    }

    [Fact]
    public void OverTheLimitThrows()
    {
        var big = new StringBuilder("class C{byte[]b={");
        while (big.Length < CodeBudget.MaxBytes + 100)
        {
            big.Append("1,");
        }
        big.Append("};}");
        var e = Assert.Throws<CartLoadException>(() => CodeBudget.Validate(Sources(big.ToString())));
        Assert.Contains("code budget exceeded", e.Message);
        Assert.Contains("262144", e.Message);
    }

    [Fact]
    public void ExactlyAtTheLimitPasses()
    {
        const string code = "class C{}";
        int codeBytes = CodeBudget.Measure(Sources(code));
        var source = new StringBuilder(code);
        source.Append(' ', CodeBudget.MaxBytes - codeBytes); // whitespace counts, unlike comments
        var sources = Sources(source.ToString());
        Assert.Equal(CodeBudget.MaxBytes, CodeBudget.Measure(sources));
        CodeBudget.Validate(sources);           // exactly 262144: allowed
        source.Append(' ');
        Assert.Throws<CartLoadException>(() => CodeBudget.Validate(Sources(source.ToString())));
    }

    [Fact]
    public void BrokenSyntaxIsStillMeasurable()
    {
        // Mid-edit hot-reload sources parse with errors; the budget must not throw.
        const string broken = "class C{int f=;}/*unterminated";
        int bytes = CodeBudget.Measure(Sources(broken));
        Assert.True(bytes > 0);
    }
}
