using System.Net;
using System.Text.RegularExpressions;

namespace JobAutopilot.Infrastructure.Himalayas;

/// <summary>
/// Turns the HTML description of an API into plain text: block elements become line breaks, list items „- ",
/// every tag is removed and entities are decoded. The result is stored and shown as text — never as HTML.
/// </summary>
internal static partial class HtmlText
{
    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = ScriptOrStyle().Replace(html, " ");
        text = ListItem().Replace(text, "\n- ");
        text = LineBreak().Replace(text, "\n");
        text = Tag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        text = SpacesBeforeNewline().Replace(text, "\n");
        text = HorizontalSpace().Replace(text, " ");
        text = BlankLines().Replace(text, "\n\n");
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 250)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase, 250)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|h[1-6]|ul|ol|tr|table|section)\s*>|<(p|div|h[1-6]|ul|ol|tr|table|section)\b[^>]*>", RegexOptions.IgnoreCase, 250)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, 250)]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t]+\n", RegexOptions.None, 250)]
    private static partial Regex SpacesBeforeNewline();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.None, 250)]
    private static partial Regex HorizontalSpace();

    [GeneratedRegex(@"\n\s*\n+", RegexOptions.None, 250)]
    private static partial Regex BlankLines();
}
