using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace JobAutopilot.Api.Features.Vacancies;

/// <summary>Body of <c>PATCH /api/vacancies/{id}</c>. Only <c>isSeen</c> can be changed; other fields are rejected.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateVacancyRequest
{
    [Required]
    public bool? IsSeen { get; init; }
}

/// <summary>Body of <c>POST /api/vacancies/seen</c>. Unknown ids are ignored.</summary>
public sealed class MarkSeenRequest
{
    public const int MaxIds = 100;

    [Required]
    [Length(1, MaxIds)]
    public IReadOnlyList<Guid>? Ids { get; init; }
}
