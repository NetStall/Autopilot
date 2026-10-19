using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Api.Features.Vacancies;

/// <summary>
/// Body of <c>PATCH /api/vacancies/{id}</c>: <c>isSeen</c> and/or <c>interest</c> (None, Starred, Dismissed).
/// Posting data comes from the source and cannot be patched; other fields are rejected.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateVacancyRequest : IValidatableObject
{
    public bool? IsSeen { get; init; }

    public string? Interest { get; init; }

    public VacancyInterest? ParsedInterest =>
        Interest is not null && TryParseInterest(Interest, out var interest) ? interest : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IsSeen is null && Interest is null)
        {
            yield return new ValidationResult("Set isSeen and/or interest.", [nameof(IsSeen), nameof(Interest)]);
        }

        if (Interest is not null && !TryParseInterest(Interest, out _))
        {
            yield return new ValidationResult(
                $"'{Interest}' is not an interest. Allowed: {string.Join(", ", Enum.GetNames<VacancyInterest>())}.",
                [nameof(Interest)]);
        }
    }

    internal static bool TryParseInterest(string value, out VacancyInterest interest)
    {
        interest = default;
        return !string.IsNullOrWhiteSpace(value)
            && !char.IsAsciiDigit(value.Trim()[0])
            && Enum.TryParse(value.Trim(), ignoreCase: true, out interest)
            && Enum.IsDefined(interest);
    }
}

/// <summary>Body of <c>POST /api/vacancies/seen</c>. Unknown ids are ignored.</summary>
public sealed class MarkSeenRequest
{
    public const int MaxIds = 100;

    [Required]
    [Length(1, MaxIds)]
    public IReadOnlyList<Guid>? Ids { get; init; }
}
