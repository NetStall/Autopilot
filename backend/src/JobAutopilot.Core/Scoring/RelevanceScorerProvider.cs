using JobAutopilot.Core.Profile;

namespace JobAutopilot.Core.Scoring;

/// <summary>Gives the scorer for the current rules (configuration + candidate profile).</summary>
public interface IRelevanceScorerProvider
{
    RelevanceScorer Scorer { get; }
}

/// <summary>Always the same scorer — for tests and tools without a profile.</summary>
public sealed class FixedRelevanceScorerProvider(RelevanceScorer scorer) : IRelevanceScorerProvider
{
    public RelevanceScorer Scorer { get; } = scorer;
}

/// <summary>
/// Positive keywords from the configuration plus the profile's skills (weight by level: Production > Project >
/// Learning; <c>None</c> adds nothing). Rebuilt when the profile file changes, so updating the profile updates the
/// relevance after „Neu bewerten". Negative keywords stay in the configuration.
/// </summary>
public sealed class ProfileRelevanceScorerProvider(RelevanceOptions options, ICandidateProfileSource profiles) : IRelevanceScorerProvider
{
    private readonly Lock _lock = new();
    private (int Version, RelevanceScorer Scorer)? _cached;

    public RelevanceScorer Scorer
    {
        get
        {
            var snapshot = profiles.Current;
            lock (_lock)
            {
                if (_cached is { } cached && cached.Version == snapshot.Version)
                {
                    return cached.Scorer;
                }

                var scorer = new RelevanceScorer(options, RulesFrom(snapshot.Usable, options.ProfileSkills));
                _cached = (snapshot.Version, scorer);
                return scorer;
            }
        }
    }

    public static IEnumerable<RelevanceRule> RulesFrom(CandidateProfile? profile, ProfileSkillWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (profile is null)
        {
            yield break;
        }

        foreach (var skill in profile.Skills)
        {
            var weight = skill.Level switch
            {
                SkillLevel.Production => weights.Production,
                SkillLevel.Project => weights.Project,
                SkillLevel.Learning => weights.Learning,
                _ => 0,
            };
            if (weight <= 0 || string.IsNullOrWhiteSpace(skill.Name))
            {
                continue;
            }

            var rule = new RelevanceRule
            {
                Name = skill.Name.Trim(),
                Weight = weight,
                Group = skill.Group ?? weights.Group,
            };
            rule.Terms.AddRange(skill.AllNames().Select(n => n.Trim()));
            yield return rule;
        }
    }
}
