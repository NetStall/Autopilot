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
        else if (filter.HiddenEmployerTypes is { Count: > 0 } hidden)
        {
            query = query.Where(v => !hidden.Contains(v.EmployerType));
        }

        if (filter.MinScore is > 0 and var minScore)
        {
            query = query.Where(v => (v.RankScore ?? v.RelevanceScore) >= minScore);
        }

        if (filter.RequiredGerman is { Count: > 0 } german)
        {
            query = query.Where(v => german.Contains(v.RequiredGerman));
        }

        if (filter.HideUnreachable)
        {
            query = query.Where(v => !v.Unreachable);
        }

        if (filter.IsSeen is { } isSeen)
        {
            query = query.Where(v => v.IsSeen == isSeen);
        }

        if (filter.Sources is { Count: > 0 } sources)
        {
            query = query.Where(v => sources.Contains(v.Source));
        }

        if (filter.RemoteLevels is { Count: > 0 } remoteLevels)
        {
            query = query.Where(v => remoteLevels.Contains(v.RemoteLevel));
        }

        if (filter.RelevanceLevels is { Count: > 0 } relevanceLevels)
        {
            // Not scored yet = "Maybe": nothing is hidden only because scoring has not run.
            var includeUnscored = relevanceLevels.Contains(RelevanceLevel.Maybe);
            var levels = relevanceLevels.Cast<RelevanceLevel?>().ToList();
            query = query.Where(v => levels.Contains(v.RelevanceLevel) || (includeUnscored && v.RelevanceLevel == null));
        }

        if (filter.Interests is { Count: > 0 } interests)
        {
            query = query.Where(v => interests.Contains(v.Interest));
        }
        else if (!filter.IncludeDismissed)
        {
            query = query.Where(v => v.Interest != VacancyInterest.Dismissed);
        }

        if (filter.FetchedSince is { } since)
        {
            var sinceUtc = since.ToUniversalTime();
            query = query.Where(v => v.FetchedAt >= sinceUtc);
        }

        if (filter.NotExpiredAt is { } activeAt)
        {
            var activeUtc = activeAt.ToUniversalTime();
            query = query.Where(v => v.ExpiresAt == null || v.ExpiresAt >= activeUtc);
        }

        if (filter.MatchedSearches is { Count: > 0 } searches)
        {
            // Array overlap (&&): found by at least one of the given searches.
            var names = searches.ToList();
            query = query.Where(v => v.MatchedSearches.Any(name => names.Contains(name)));
        }

        return query;
    }

    /// <summary>
    /// Shows each job once: of all vacancies with the same <see cref="Vacancy.DuplicateKey"/> in
    /// <paramref name="pool"/>, only the one imported first is kept (ties: lower source/external id). The others are
    /// listed with it as "also found at". The pool is all vacancies — so dismissing the first one also hides its
    /// duplicates — or, with a source filter, the vacancies of that source.
    /// </summary>
    public static IQueryable<Vacancy> CollapseDuplicates(this IQueryable<Vacancy> query, IQueryable<Vacancy> pool) =>
        query.Where(v => v.DuplicateKey == null || !pool.Any(o =>
            o.DuplicateKey == v.DuplicateKey
            && o.Id != v.Id
            && (o.FetchedAt < v.FetchedAt
                || (o.FetchedAt == v.FetchedAt && string.Compare(o.ExternalId, v.ExternalId) < 0))));

    /// <summary>Stable ordering for paging; ties are broken by import time and id.</summary>
    public static IOrderedQueryable<Vacancy> ApplySort(this IQueryable<Vacancy> query, VacancySort sort) => sort switch
    {
        // Direct employers before unknown ones before agencies / temporary work, at the same score.
        // The rank (relevance minus fit penalties) when known, otherwise the relevance score.
        VacancySort.Relevance => query
            .OrderBy(v => v.RelevanceScore == null)
            .ThenByDescending(v => v.RankScore ?? v.RelevanceScore)
            .ThenBy(v => v.EmployerType == EmployerType.Direct ? 0
                : v.EmployerType == EmployerType.Unknown ? 1
                : v.EmployerType == EmployerType.Agency ? 2
                : 3)
            .ThenBy(v => v.PublishedAt == null)
            .ThenByDescending(v => v.PublishedAt)
            .ThenByDescending(v => v.FetchedAt)
            .ThenByDescending(v => v.Id),
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
