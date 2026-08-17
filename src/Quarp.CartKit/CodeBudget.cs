using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Quarp.CartKit;

/// <summary>
/// The 64 KB code limit (SPEC-8 §6): UTF-8 byte count of all sources after stripping
/// comment trivia via a Roslyn syntax tree (comments are free by design — the limit must
/// not punish readability) and normalizing \r\n to \n.
/// </summary>
public static class CodeBudget
{
    public const int MaxBytes = 65536;

    /// <summary>Total budgeted bytes across all sources.</summary>
    public static int Measure(IReadOnlyList<CartSourceFile> sources)
    {
        int total = 0;
        for (int i = 0; i < sources.Count; i++)
        {
            total += MeasureOne(sources[i].Text);
        }
        return total;
    }

    /// <summary>Throws <see cref="CartLoadException"/> when the sources exceed the budget.</summary>
    public static void Validate(IReadOnlyList<CartSourceFile> sources)
    {
        int total = Measure(sources);
        if (total > MaxBytes)
        {
            throw new CartLoadException(
                $"code budget exceeded: {total} bytes of source after comment removal, the limit is {MaxBytes} "
                + "(SPEC-8 §6: comments are free, code is not).");
        }
    }

    private static int MeasureOne(string text)
    {
        // Parsing succeeds even for sources with syntax errors, so the budget is
        // measurable at any moment of a hot-reload editing session.
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text);
        SyntaxNode root = tree.GetRoot();
        var builder = new StringBuilder(text.Length);
        int position = 0;
        foreach (SyntaxTrivia trivia in root.DescendantTrivia())
        {
            switch (trivia.Kind())
            {
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.SingleLineDocumentationCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                    builder.Append(text, position, trivia.SpanStart - position);
                    position = trivia.Span.End;
                    break;
            }
        }
        builder.Append(text, position, text.Length - position);
        builder.Replace("\r\n", "\n");
        return Encoding.UTF8.GetByteCount(builder.ToString());
    }
}
