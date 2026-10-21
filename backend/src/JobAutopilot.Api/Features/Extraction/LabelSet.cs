using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Extraction;

/// <summary>
/// Explicit and implicit labels (<see cref="ImplicitLabels"/>) of all vacancies, or of the given ones (a list page). An
/// application counts for its vacancy and the vacancy's duplicates.
/// </summary>
internal static class LabelSet
{
    public static async Task<Dictionary<Guid, VacancyLabel>> LoadAsync(AppDbContext db, IReadOnlyCollection<Guid>? ids, CancellationToken ct)
    {
        var vacancies = ids is null ? db.Vacancies.AsNoTracking() : db.Vacancies.AsNoTracking().Where(v => ids.Contains(v.Id));
        var explicitLabels = await (ids is null ? db.GoldLabels.AsNoTracking() : db.GoldLabels.AsNoTracking().Where(l => ids.Contains(l.VacancyId)))
            .ToDictionaryAsync(l => l.VacancyId, l => l.Fits, ct);

        var applications = await db.Applications.AsNoTracking()
            .Where(a => a.Status != ApplicationStatus.Draft && a.Status != ApplicationStatus.Approved)
            .Select(a => new { a.VacancyId, a.DuplicateKey })
            .ToListAsync(ct);
        var appliedIds = applications.Where(a => a.VacancyId != null).Select(a => a.VacancyId!.Value).ToList();
        var appliedKeys = applications.Where(a => a.DuplicateKey != null).Select(a => a.DuplicateKey!).ToList();
        var applied = await vacancies
            .Where(v => appliedIds.Contains(v.Id) || (v.DuplicateKey != null && appliedKeys.Contains(v.DuplicateKey)))
            .Select(v => v.Id)
            .ToListAsync(ct);
        var dismissed = await vacancies.Where(v => v.Interest == VacancyInterest.Dismissed).Select(v => v.Id).ToListAsync(ct);

        var implicitLabels = dismissed.ToDictionary(id => id, id => ImplicitLabels.For(false, VacancyInterest.Dismissed));
        foreach (var id in applied)
        {
            implicitLabels[id] = ImplicitLabels.For(true, VacancyInterest.None);
        }

        return explicitLabels.Keys.Union(implicitLabels.Keys)
            .Select(id => ImplicitLabels.Combine(id, explicitLabels.TryGetValue(id, out var e) ? e : null, implicitLabels.GetValueOrDefault(id)))
            .OfType<VacancyLabel>()
            .ToDictionary(l => l.VacancyId);
    }
}
