using System.Globalization;
using System.Text;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.SearchProfiles;

/// <summary>Validation, the generated import searches and the change summary of a <see cref="SearchProfile"/>.</summary>
public static class SearchProfileRules
{
    public const int MaxTerms = 30;
    public const int MaxPlaces = 10;
    public const int MaxKeywords = 80;
    public const int MaxPenalty = 50;

    /// <summary>Human-readable problems (German, for the form); empty when valid.</summary>
    public static IReadOnlyList<string> Validate(SearchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<string>();

        var terms = profile.SearchTerms.Select(t => t.Text?.Trim() ?? string.Empty).ToList();
        if (terms.Count == 0 || terms.All(t => t.Length == 0))
        {
            errors.Add("Suchbegriffe: mindestens ein Suchbegriff ist nötig.");
        }

        if (terms.Any(t => t.Length == 0))
        {
            errors.Add("Suchbegriffe: leere Einträge entfernen.");
        }

        if (terms.Count > MaxTerms)
        {
            errors.Add($"Suchbegriffe: höchstens {MaxTerms}.");
        }

        if (terms.Any(t => t.Length > VacancySearchRules.KeywordMaxLength))
        {
            errors.Add($"Suchbegriffe: höchstens {VacancySearchRules.KeywordMaxLength} Zeichen je Begriff.");
        }

        errors.AddRange(Duplicates(terms, "Suchbegriffe"));

        if (profile.SearchTerms.Any(t => !Enum.IsDefined(t.Scope)))
        {
            errors.Add("Suchbegriffe: unbekannter Bereich.");
        }

        var cities = profile.Places.Select(p => p.City?.Trim() ?? string.Empty).ToList();
        if (cities.Any(c => c.Length == 0))
        {
            errors.Add("Orte: Ort darf nicht leer sein.");
        }

        if (profile.Places.Count > MaxPlaces)
        {
            errors.Add($"Orte: höchstens {MaxPlaces}.");
        }

        errors.AddRange(Duplicates(cities, "Orte"));
        if (profile.Places.Any(p => p.RadiusKm is < 0 or > VacancySearchRules.MaxRadiusKm))
        {
            errors.Add($"Orte: Umkreis zwischen 0 und {VacancySearchRules.MaxRadiusKm} km.");
        }

        errors.AddRange(Duplicates(profile.AdditionalPlaces.Select(p => p.Trim()).ToList(), "Weitere Orte"));

        var hasRegionTerms = profile.SearchTerms.Any(t => t.Scope != SearchTermScope.Remote);
        var hasRemoteTerms = profile.SearchTerms.Any(t => t.Scope != SearchTermScope.Region);
        if (profile.Places.Count == 0 && !profile.RemoteGermany)
        {
            errors.Add("Orte: mindestens ein Ort oder „Remote deutschlandweit“ ist nötig.");
        }
        else if (hasRegionTerms && profile.Places.Count == 0 && !hasRemoteTerms)
        {
            errors.Add("Suchbegriffe sind nur für die Region eingetragen, aber es gibt keinen Ort.");
        }

        if (!profile.Sources.Arbeitsagentur && !profile.Sources.Himalayas)
        {
            errors.Add("Quellen: mindestens eine Quelle einschalten.");
        }

        foreach (var (list, name, min, max) in new[]
                 {
                     (profile.MustHave, "Must-have", 1, 50), (profile.NiceToHave, "Nice-to-have", 1, 50),
                     (profile.Exclude, "Ausschluss", 1, 100),
                 })
        {
            if (list.Count > MaxKeywords)
            {
                errors.Add($"{name}: höchstens {MaxKeywords} Einträge.");
            }

            if (list.Any(k => string.IsNullOrWhiteSpace(k.Name)))
            {
                errors.Add($"{name}: leere Einträge entfernen.");
            }

            if (list.Any(k => k.Weight < min || k.Weight > max))
            {
                errors.Add($"{name}: Gewicht zwischen {min} und {max}.");
            }

            errors.AddRange(Duplicates(list.Select(k => k.Name.Trim()).ToList(), name));
        }

        if (profile.TitleExclusions.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Titel-Ausschlüsse: leere Einträge entfernen.");
        }

        errors.AddRange(Duplicates(profile.TitleExclusions.Select(t => t.Trim()).ToList(), "Titel-Ausschlüsse"));

        if (!Enum.IsDefined(profile.Agency) || !Enum.IsDefined(profile.Temporary) || !Enum.IsDefined(profile.RemotePreference))
        {
            errors.Add("Unbekannter Wert bei Arbeitgebertypen oder Remote-Präferenz.");
        }

        if (profile.PublishedWithinDays is < 1 or > VacancySearchRules.MaxPublishedWithinDays)
        {
            errors.Add($"Veröffentlicht innerhalb: 1 bis {VacancySearchRules.MaxPublishedWithinDays} Tage.");
        }

        if (profile.MinScore is < 0 or > 100 || profile.MinScore % 5 != 0)
        {
            errors.Add("Mindestbewertung: 0 bis 100 in 5er-Schritten.");
        }

        if (profile.GermanFilter.Any(g => !Enum.IsDefined(g)) || profile.GermanFilter.Count > Enum.GetValues<Vacancies.RequiredGerman>().Length)
        {
            errors.Add("Deutsch-Filter: unbekannter Wert.");
        }

        if (profile.TopN is < 1 or > 200)
        {
            errors.Add("Top N: 1 bis 200.");
        }

        var p = profile.Penalties;
        foreach (var (value, name) in new[]
                 {
                     (p.GermanPerLevel, "Deutsch je Stufe"), (p.Experience, "Erfahrung/Senior"), (p.ExperienceStrong, "Erfahrung (deutlich)"),
                     (p.MustHaveGap, "Lücke Muss"), (p.RequiredGap, "Lücke"), (p.LikelyRequiredGap, "Lücke Tech-Stack"),
                     (p.NiceToHaveGap, "Lücke Plus"), (p.MustHaveGapMax, "Muss-Lücken gesamt"), (p.RequiredGapMax, "Lücken gesamt"),
                     (p.OutsideRegion, "Außerhalb der Region"), (p.OutsideRegionUnknownShare, "Außerhalb, Homeoffice unbekannt"),
                     (p.RemoteByArrangement, "Remote nur nach Absprache"),
                     (p.Unreachable, "Nicht erreichbar"),
                     (p.EmployerType, "Arbeitgebertyp"), (p.RemotePreference, "Remote-Wunsch"),
                 })
        {
            if (value is < 0 or > MaxPenalty)
            {
                errors.Add($"Fit-Abzüge: „{name}\" zwischen 0 und {MaxPenalty}.");
            }
        }

        if (p.RequiredGapMax < p.RequiredGap)
        {
            errors.Add("Fit-Abzüge: „Lücken gesamt\" muss mindestens so groß sein wie „Lücke\".");
        }

        if (p.MustHaveGapMax < p.MustHaveGap)
        {
            errors.Add("Fit-Abzüge: „Muss-Lücken gesamt\" muss mindestens so groß sein wie „Lücke Muss\".");
        }

        if (profile.SalaryMin is < 0 || profile.SalaryMax is < 0 || profile.SalaryMin > profile.SalaryMax)
        {
            errors.Add("Gehaltsziel: von ≤ bis, keine negativen Werte.");
        }

        if (profile.EarliestStart is { Length: > 0 } start
            && !DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add("Frühester Eintritt: Datum als JJJJ-MM-TT.");
        }

        if (profile.MaxCommuteMinutes is < 0 or > 600)
        {
            errors.Add("Pendelzeit: 0 bis 600 Minuten.");
        }

        if (errors.Count == 0)
        {
            errors.AddRange(VacancySearchRules.Validate(GenerateSearches(profile)).Select(e => $"Erzeugte Suche: {e}"));
        }

        return errors;
    }

    /// <summary>
    /// The import searches: every region term around every place (Arbeitsagentur), and with „Remote deutschlandweit"
    /// every remote term as an Arbeitsagentur home-office search and a Himalayas search — only for enabled sources.
    /// Names are stable (<c>region-…</c>, <c>remote-…</c>, <c>himalayas-…</c>) so vacancies keep their matched searches.
    /// </summary>
    public static IReadOnlyList<VacancySearch> GenerateSearches(SearchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var searches = new List<VacancySearch>();
        var terms = profile.SearchTerms.Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList();
        var singlePlace = profile.Places.Count == 1;

        if (profile.Sources.Arbeitsagentur)
        {
            foreach (var place in profile.Places)
            {
                foreach (var term in terms.Where(t => t.Scope != SearchTermScope.Remote))
                {
                    var name = singlePlace ? $"region-{Slug(term.Text)}" : $"region-{Slug(place.City)}-{Slug(term.Text)}";
                    searches.Add(new VacancySearch
                    {
                        Name = Limit(name), Keyword = term.Text.Trim(), Location = place.City.Trim(), RadiusKm = place.RadiusKm,
                        PublishedWithinDays = profile.PublishedWithinDays,
                    });
                }
            }
        }

        if (profile.RemoteGermany)
        {
            foreach (var term in terms.Where(t => t.Scope != SearchTermScope.Region))
            {
                if (profile.Sources.Arbeitsagentur)
                {
                    searches.Add(new VacancySearch
                    {
                        Name = Limit($"remote-{Slug(term.Text)}"), Keyword = term.Text.Trim(), RemoteOnly = true,
                        PublishedWithinDays = profile.PublishedWithinDays,
                    });
                }

                if (profile.Sources.Himalayas)
                {
                    searches.Add(new VacancySearch
                    {
                        Name = Limit($"himalayas-{Slug(term.Text)}"), Keyword = term.Text.Trim(), RemoteOnly = true,
                        Source = VacancySource.Himalayas, PublishedWithinDays = profile.PublishedWithinDays,
                    });
                }
            }
        }

        // Two terms with the same slug (e.g. "C#" and "c#") would clash; the first one wins.
        return [.. searches.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())];
    }

    /// <summary>„C#" → <c>csharp</c>, „.NET Entwickler" → <c>dotnet-entwickler</c>, „Full Stack" → <c>full-stack</c>.</summary>
    public static string Slug(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Trim().ToLowerInvariant()
            .Replace("c#", "csharp", StringComparison.Ordinal)
            .Replace("c++", "cplusplus", StringComparison.Ordinal)
            .Replace(".net", "dotnet", StringComparison.Ordinal)
            .Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal).Replace("ß", "ss", StringComparison.Ordinal);
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>A short German summary of what changed, for the history („Suchbegriffe: +Blazor, −Vue; Mindestbewertung 50 → 60").</summary>
    public static string Summarize(SearchProfile? before, SearchProfile after)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (before is null)
        {
            return "Angelegt";
        }

        var parts = new List<string>();
        ListDiff(parts, "Suchbegriffe", before.SearchTerms.Select(t => $"{t.Text} ({t.Scope})"), after.SearchTerms.Select(t => $"{t.Text} ({t.Scope})"));
        ListDiff(parts, "Orte", before.Places.Select(p => $"{p.City} {p.RadiusKm} km"), after.Places.Select(p => $"{p.City} {p.RadiusKm} km"));
        ListDiff(parts, "Weitere Orte", before.AdditionalPlaces, after.AdditionalPlaces);
        ListDiff(parts, "Must-have", before.MustHave.Select(Key), after.MustHave.Select(Key));
        ListDiff(parts, "Nice-to-have", before.NiceToHave.Select(Key), after.NiceToHave.Select(Key));
        ListDiff(parts, "Ausschluss", before.Exclude.Select(Key), after.Exclude.Select(Key));
        ListDiff(parts, "Titel-Ausschlüsse", before.TitleExclusions, after.TitleExclusions);
        Value(parts, "Remote deutschlandweit", before.RemoteGermany, after.RemoteGermany);
        Value(parts, "Remote-Präferenz", before.RemotePreference, after.RemotePreference);
        Value(parts, "Agentur", before.Agency, after.Agency);
        Value(parts, "Zeitarbeit", before.Temporary, after.Temporary);
        Value(parts, "Veröffentlicht innerhalb", before.PublishedWithinDays, after.PublishedWithinDays);
        Value(parts, "Arbeitsagentur", before.Sources.Arbeitsagentur, after.Sources.Arbeitsagentur);
        Value(parts, "Himalayas", before.Sources.Himalayas, after.Sources.Himalayas);
        Value(parts, "Mindestbewertung", before.MinScore, after.MinScore);
        Value(parts, "Top N", before.TopN, after.TopN);
        ListDiff(parts, "Deutsch-Filter", before.GermanFilter.Select(g => g.ToString()), after.GermanFilter.Select(g => g.ToString()));
        Value(parts, "Nicht erreichbare ausblenden", before.HideUnreachable, after.HideUnreachable);
        Value(parts, "Abzug Deutsch", before.Penalties.GermanPerLevel, after.Penalties.GermanPerLevel);
        Value(parts, "Abzug Erfahrung", before.Penalties.Experience, after.Penalties.Experience);
        Value(parts, "Abzug Erfahrung (deutlich)", before.Penalties.ExperienceStrong, after.Penalties.ExperienceStrong);
        Value(parts, "Abzug Lücke Muss", before.Penalties.MustHaveGap, after.Penalties.MustHaveGap);
        Value(parts, "Abzug Lücke", before.Penalties.RequiredGap, after.Penalties.RequiredGap);
        Value(parts, "Abzug Lücke Tech-Stack", before.Penalties.LikelyRequiredGap, after.Penalties.LikelyRequiredGap);
        Value(parts, "Abzug Lücke Plus", before.Penalties.NiceToHaveGap, after.Penalties.NiceToHaveGap);
        Value(parts, "Abzug Muss-Lücken gesamt", before.Penalties.MustHaveGapMax, after.Penalties.MustHaveGapMax);
        Value(parts, "Abzug Lücken gesamt", before.Penalties.RequiredGapMax, after.Penalties.RequiredGapMax);
        Value(parts, "Abzug Region", before.Penalties.OutsideRegion, after.Penalties.OutsideRegion);
        Value(parts, "Abzug außerhalb, Homeoffice unbekannt", before.Penalties.OutsideRegionUnknownShare, after.Penalties.OutsideRegionUnknownShare);
        Value(parts, "Abzug Remote nur nach Absprache", before.Penalties.RemoteByArrangement, after.Penalties.RemoteByArrangement);
        Value(parts, "Abzug nicht erreichbar", before.Penalties.Unreachable, after.Penalties.Unreachable);
        Value(parts, "Abzug Arbeitgebertyp", before.Penalties.EmployerType, after.Penalties.EmployerType);
        Value(parts, "Abzug Remote-Wunsch", before.Penalties.RemotePreference, after.Penalties.RemotePreference);
        Value(parts, "Gehalt von", before.SalaryMin, after.SalaryMin);
        Value(parts, "Gehalt bis", before.SalaryMax, after.SalaryMax);
        Value(parts, "Frühester Eintritt", before.EarliestStart, after.EarliestStart);
        Value(parts, "Pendelzeit", before.MaxCommuteMinutes, after.MaxCommuteMinutes);
        return parts.Count == 0 ? "Keine Änderung" : string.Join("; ", parts);

        static string Key(StackKeyword k) => $"{k.Name} ({k.Weight})";
    }

    private static void ListDiff(List<string> parts, string label, IEnumerable<string> before, IEnumerable<string> after)
    {
        var b = before.ToList();
        var a = after.ToList();
        var added = a.Except(b, StringComparer.Ordinal).Select(x => "+" + x);
        var removed = b.Except(a, StringComparer.Ordinal).Select(x => "−" + x);
        var changes = added.Concat(removed).ToList();
        if (changes.Count > 0)
        {
            parts.Add($"{label}: {string.Join(", ", changes.Take(8))}{(changes.Count > 8 ? " …" : string.Empty)}");
        }
    }

    private static void Value<T>(List<string> parts, string label, T before, T after)
    {
        if (!EqualityComparer<T>.Default.Equals(before, after))
        {
            parts.Add($"{label} {Format(before)} → {Format(after)}");
        }

        static string Format(T value) => value switch
        {
            null => "–",
            bool flag => flag ? "an" : "aus",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "–",
        };
    }

    private static IEnumerable<string> Duplicates(List<string> values, string label) =>
        values.Where(v => v.Length > 0)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{label}: „{g.Key}\" ist doppelt.");

    private static string Limit(string name) =>
        name.Length <= VacancySearchRules.NameMaxLength ? name : name[..VacancySearchRules.NameMaxLength].TrimEnd('-');
}
