using System.Globalization;

namespace JobAutopilot.Core.Profile;

/// <summary>Problems in a profile. Errors make it unusable; warnings are shown but the profile is used.</summary>
public sealed record ProfileValidation(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Checks a loaded profile: required fields, links, dates, gap phrasings. Pure and unit-tested.</summary>
public static class ProfileValidator
{
    public static ProfileValidation Validate(CandidateProfile profile, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Personal.Name))
        {
            errors.Add("personal.name fehlt.");
        }

        if (string.IsNullOrWhiteSpace(profile.Personal.Email) || !profile.Personal.Email.Contains('@', StringComparison.Ordinal))
        {
            errors.Add("personal.email fehlt oder ist keine E-Mail-Adresse.");
        }

        foreach (var (name, value) in new[]
                 {
                     ("website", profile.Personal.Website), ("github", profile.Personal.Github),
                     ("linkedin", profile.Personal.Linkedin),
                 })
        {
            if (value is not null && !IsHttpUrl(value))
            {
                errors.Add($"personal.{name} muss ein http(s)-Link sein.");
            }
        }

        foreach (var (project, i) in profile.Projects.Select((p, i) => (p, i)))
        {
            if (project.Url is not null && !IsHttpUrl(project.Url))
            {
                errors.Add($"projects[{i}].url muss ein http(s)-Link sein.");
            }
        }

        var periods = new List<(int Index, DateOnly From, DateOnly To)>();
        foreach (var (entry, i) in profile.Experience.Select((e, i) => (e, i)))
        {
            if (string.IsNullOrWhiteSpace(entry.Company) || string.IsNullOrWhiteSpace(entry.Title))
            {
                errors.Add($"experience[{i}]: company und title sind Pflicht.");
            }

            if (CheckPeriod($"experience[{i}]", entry.From, entry.To, today, errors) is { } period)
            {
                periods.Add((i, period.From, period.To));
            }
        }

        foreach (var (entry, i) in profile.Education.Select((e, i) => (e, i)))
        {
            CheckPeriod($"education[{i}]", entry.From, entry.To, today, errors);
        }

        // Overlapping jobs are possible (side job), so this is only a hint; the month of a job change does not count.
        for (var a = 0; a < periods.Count; a++)
        {
            for (var b = a + 1; b < periods.Count; b++)
            {
                var overlapFrom = periods[a].From > periods[b].From ? periods[a].From : periods[b].From;
                var overlapTo = periods[a].To < periods[b].To ? periods[a].To : periods[b].To;
                if (overlapFrom.AddMonths(1) < overlapTo)
                {
                    warnings.Add($"experience[{periods[a].Index}] und experience[{periods[b].Index}] überschneiden sich.");
                }
            }
        }

        foreach (var (skill, i) in profile.Skills.Select((s, i) => (s, i)))
        {
            if (string.IsNullOrWhiteSpace(skill.Name))
            {
                errors.Add($"skills[{i}].name fehlt.");
            }

            if (!Enum.IsDefined(skill.Level))
            {
                errors.Add($"skills[{i}].level ist unbekannt.");
            }

            if (skill.Years is < 0 or > 50)
            {
                errors.Add($"skills[{i}].years muss zwischen 0 und 50 liegen.");
            }
        }

        warnings.AddRange(profile.Skills
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"Skill „{g.Key}\" ist mehrfach eingetragen."));

        foreach (var (gap, i) in profile.KnownGaps.Select((g, i) => (g, i)))
        {
            if (string.IsNullOrWhiteSpace(gap.Skill))
            {
                errors.Add($"knownGaps[{i}].skill fehlt.");
            }

            if (string.IsNullOrWhiteSpace(gap.De) || string.IsNullOrWhiteSpace(gap.En))
            {
                errors.Add($"knownGaps[{i}] ({gap.Skill}): die ehrliche Formulierung fehlt auf Deutsch (de) oder Englisch (en).");
            }
        }

        foreach (var (language, i) in profile.Languages.Select((l, i) => (l, i)))
        {
            if (!Enum.IsDefined(language.Level))
            {
                errors.Add($"languages[{i}].level ist unbekannt.");
            }
        }

        var prefs = profile.Preferences;
        if (prefs.SalaryMin is { } min && prefs.SalaryMax is { } max && min > max)
        {
            errors.Add("preferences: salaryMin ist größer als salaryMax.");
        }

        if (prefs.Remote is not ("Remote" or "Hybrid" or "OnSite" or "Any"))
        {
            errors.Add("preferences.remote muss Remote, Hybrid, OnSite oder Any sein.");
        }

        if (prefs.EarliestStart is { } start
            && !DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add("preferences.earliestStart muss YYYY-MM-DD sein.");
        }

        if (profile.Experience.Count == 0)
        {
            warnings.Add("Keine Berufserfahrung eingetragen.");
        }

        return new ProfileValidation(errors, warnings);
    }

    /// <summary>Parses <c>YYYY-MM</c> as the first day of that month.</summary>
    public static bool TryParseMonth(string? value, out DateOnly month) =>
        DateOnly.TryParseExact(value?.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out month);

    /// <summary>Months of professional experience; overlapping periods count once.</summary>
    public static int ExperienceMonths(CandidateProfile profile, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var months = new HashSet<int>();
        foreach (var entry in profile.Experience)
        {
            if (!TryParseMonth(entry.From, out var from))
            {
                continue;
            }

            var to = entry.To is null ? new DateOnly(today.Year, today.Month, 1)
                : TryParseMonth(entry.To, out var end) ? end : from;
            for (var m = from; m <= to; m = m.AddMonths(1))
            {
                months.Add((m.Year * 12) + m.Month);
            }
        }

        return months.Count;
    }

    private static (DateOnly From, DateOnly To)? CheckPeriod(string label, string from, string? to, DateOnly today, List<string> errors)
    {
        if (!TryParseMonth(from, out var start))
        {
            errors.Add($"{label}.from muss YYYY-MM sein.");
            return null;
        }

        var end = new DateOnly(today.Year, today.Month, 1);
        if (to is not null && !TryParseMonth(to, out end))
        {
            errors.Add($"{label}.to muss YYYY-MM sein (oder fehlen = bis heute).");
            return null;
        }

        if (start > end)
        {
            errors.Add($"{label}: from liegt nach to.");
            return null;
        }

        return (start, end.AddMonths(1));
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
