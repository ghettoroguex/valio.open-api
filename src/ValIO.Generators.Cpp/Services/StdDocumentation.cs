using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;

namespace ValIO.Generators.Cpp.Services;

/// <summary>Builds Doxygen documentation even when a schema omits its prose descriptions.</summary>
internal static class StdDocumentation
{
    /// <summary>Combines a useful generated brief with the original schema documentation.</summary>
    /// <param name="brief">The generated declaration summary.</param>
    /// <param name="description">The optional original schema description.</param>
    /// <param name="details">Additional Doxygen lines describing parameters, results, or ownership.</param>
    /// <returns>The AST documentation comment.</returns>
    public static CommentNode Create(string brief, string? description = null, params string[] details)
    {
        var lines = new List<string> { "@brief " + brief };
        if (!string.IsNullOrWhiteSpace(description))
            lines.Add("@details " + description);
        lines.AddRange(details);
        return new CommentNode(string.Join('\n', lines), CommentKind.MultiLine);
    }
}
