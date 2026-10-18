namespace JobAutopilot.Core.Import;

/// <summary>Validation rules for searches, shared by the configuration check and the ad-hoc import request.</summary>
public static class VacancySearchRules
{
    public const int NameMaxLength = 50;
    public const int KeywordMaxLength = 200;
    public const int LocationMaxLength = 200;
    public const int MaxRadiusKm = 200;
    public const int MaxPublishedWithinDays = 100;
    public const int MaxPagesLimit = 50;

    /// <summary>Returns human-readable problems with one search; empty when it is valid.</summary>
    public static IReadOnlyList<string> Validate(VacancySearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(search.Name))
        {
            errors.Add("Name is required.");
        }
        else if (search.Name.Length > NameMaxLength || !search.Name.All(IsNameChar))
        {
            errors.Add($"Name must be at most {NameMaxLength} letters, digits, '-' or '_'.");
        }

        if (string.IsNullOrWhiteSpace(search.Keyword))
        {
            errors.Add("Keyword is required.");
        }
        else if (search.Keyword.Length > KeywordMaxLength)
        {
            errors.Add($"Keyword must be at most {KeywordMaxLength} characters.");
        }

        if (search.Location is { Length: > LocationMaxLength })
        {
            errors.Add($"Location must be at most {LocationMaxLength} characters.");
        }

        if (search.RadiusKm is not null && string.IsNullOrWhiteSpace(search.Location))
        {
            errors.Add("RadiusKm requires a Location (without Location the search covers all of Germany).");
        }

        if (search.RadiusKm is < 0 or > MaxRadiusKm)
        {
            errors.Add($"RadiusKm must be between 0 and {MaxRadiusKm}.");
        }

        if (search.PublishedWithinDays is < 0 or > MaxPublishedWithinDays)
        {
            errors.Add($"PublishedWithinDays must be between 0 and {MaxPublishedWithinDays}.");
        }

        if (search.MaxPages is < 1 or > MaxPagesLimit)
        {
            errors.Add($"MaxPages must be between 1 and {MaxPagesLimit}.");
        }

        return errors;
    }

    /// <summary>Validates every search and that names are unique (case-insensitive).</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<VacancySearch> searches)
    {
        ArgumentNullException.ThrowIfNull(searches);
        var errors = new List<string>();

        for (var i = 0; i < searches.Count; i++)
        {
            var label = string.IsNullOrWhiteSpace(searches[i].Name) ? $"#{i + 1}" : $"'{searches[i].Name}'";
            errors.AddRange(Validate(searches[i]).Select(e => $"Search {label}: {e}"));
        }

        errors.AddRange(searches
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"Search name '{g.Key}' is used {g.Count()} times; names must be unique."));

        return errors;
    }

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}
