using JobAutopilot.Core.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Infrastructure.Persistence;

public static class VacancyQueryExtensions
{
    private const string LikeEscape = "\\";

    /// <summary>Applies <paramref name="filter"/>; text criteria are case-insensitive substring matches (ILIKE).</summary>
    public static IQueryable<Vacancy> ApplyFilter(this IQueryable<Vacancy> query, VacancyFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = ContainsPattern(filter.Text);
            query = query.Where(v =>
                EF.Functions.ILike(v.Title, pattern, LikeEscape) || EF.Functions.ILike(v.Company, pattern, LikeEscape));
        }

        if (!string.IsNullOrWhiteSpace(filter.Location))
        {
            var pattern = ContainsPattern(filter.Location);
            query = query.Where(v => v.Location != null && EF.Functions.ILike(v.Location, pattern, LikeEscape));
        }

        if (filter.EmployerTypes is { Count: > 0 } types)
        {
            query = query.Where(v => types.Contains(v.EmployerType));
        }

        if (filter.IsSeen is { } isSeen)
        {
            query = query.Where(v => v.IsSeen == isSeen);
        }

        if (filter.Source is { } source)
        {
            query = query.Where(v => v.Source == source);
        }

        if (filter.IsRemote is { } isRemote)
        {
            query = query.Where(v => v.IsRemote == isRemote);
        }

        if (filter.MatchedSearches is { Count: > 0 } searches)
        {
            // Array overlap (&&): found by at least one of the given searches.
            var names = searches.ToList();
            query = query.Where(v => v.MatchedSearches.Any(name => names.Contains(name)));
        }

        return query;
    }

    /// <summary>Stable ordering for paging; ties are broken by import time and id.</summary>
    public static IOrderedQueryable<Vacancy> ApplySort(this IQueryable<Vacancy> query, VacancySort sort) => sort switch
    {
        VacancySort.Fetched => query
            .OrderByDescending(v => v.FetchedAt)
            .ThenByDescending(v => v.Id),
        _ => query
            .OrderBy(v => v.PublishedAt == null)
            .ThenByDescending(v => v.PublishedAt)
            .ThenByDescending(v => v.FetchedAt)
            .ThenByDescending(v => v.Id),
    };

    /// <summary>User input is matched literally: <c>%</c>, <c>_</c> and <c>\</c> are escaped.</summary>
    internal static string ContainsPattern(string text)
    {
        var escaped = text.Trim()
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
