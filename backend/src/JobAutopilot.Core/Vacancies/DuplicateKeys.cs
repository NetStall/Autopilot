using System.Text;
using System.Text.RegularExpressions;

namespace JobAutopilot.Core.Vacancies;

/// <summary>
/// The key that recognises the same job posted in several places (two sources, or one source twice): company and
/// title, lower-case, without gender markers like „(m/w/d)" and legal forms like „GmbH", punctuation as spaces.
/// </summary>
public static partial class DuplicateKeys
{
    public const int MaxLength = 400;

    public static string For(string company, string title)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(title);
        var key = $"{NormalizeCompany(company)}|{NormalizeTitle(title)}";
        return key.Length <= MaxLength ? key : key[..MaxLength];
    }

    internal static string NormalizeTitle(string title) =>
        Collapse(GenderMarker().Replace(title.ToLowerInvariant(), " "));

    internal static string NormalizeCompany(string company)
    {
        var lower = Collapse(company.ToLowerInvariant());
        // Repeated, so "… GmbH & Co. KG" loses all parts.
        string previous;
        do
        {
            previous = lower;
            lower = Collapse(LegalForm().Replace(lower, " "));
        }
        while (lower != previous);

        return lower;
    }

    /// <summary>Letters and digits only (plus # and + for C#, C++), single spaces.</summary>
    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var space = false;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '#' or '+')
            {
                if (space && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(c);
                space = false;
            }
            else
            {
                space = true;
            }
        }

        return builder.ToString();
    }

    // (m/w/d), (w/m/d), m/w/d, (m/f/d), (f/m/x), (m/w/x), (gn), (all genders), (d/m/w), (m/w/div) …
    [GeneratedRegex(
        @"\(?\b(?:[mwfdx]|div|i|a)\s*/\s*(?:[mwfdx]|div|i|a)(?:\s*/\s*(?:[mwfdx]|div|i|a))*\b\)?|\((?:gn|all genders|alle geschlechter|m/w/d\*?)\)|\ball genders\b",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex GenderMarker();

    [GeneratedRegex(
        @"(?:^| )(?:gmbh|mbh|ag|se|kg|kgaa|ohg|gbr|ug|haftungsbeschränkt|e v|ev|eg|co|inc|ltd|llc|plc|bv|b v|nv|sarl|s a|sa|sp z o o)(?= |$)",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex LegalForm();
}
