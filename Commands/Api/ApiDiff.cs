using System.Text.RegularExpressions;
using Catalyst.Common.Models;

namespace Catalyst.Commands.Api;

/// <summary>
/// Compares two <see cref="ClientModel"/>s and reports what changes for code written against the generated client:
/// call names, parameter lists and TypeScript types, not raw spec differences. Type strings are compared as text after
/// collapsing whitespace, so a renamed type shows as a changed type plus a removed and an added type.
/// </summary>
internal static partial class ApiDiff
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static List<ApiChange> Compare(ClientModel before, ClientModel after)
    {
        List<ApiChange> changes = [];
        OperationDiff.Compare(before, after, changes);
        TypeDiff.Compare(before, after, changes);
        return changes;
    }

    internal static string Normalize(string type) => Whitespace().Replace(type.Trim(), " ");

    /// <summary>Splits a type at its top-level "|" (not inside brackets, generics or string literals).</summary>
    internal static List<string> SplitUnion(string type)
    {
        List<string> parts = [];
        int depth = 0;
        char quote = '\0';
        int start = 0;

        for (int i = 0; i < type.Length; i++)
        {
            char c = type[i];

            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    quote = c;
                    break;
                case '{' or '(' or '<' or '[':
                    depth++;
                    break;
                case '}' or ')' or '>' or ']':
                    depth--;
                    break;
                case '|' when depth == 0:
                    parts.Add(type[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        parts.Add(type[start..].Trim());
        return parts.Where(p => p.Length > 0).ToList();
    }
}
