using JobAutopilot.Core.Profile;
using JobAutopilot.Core.SearchProfiles;

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
/// The keywords of the Suchprofil (must-have, nice-to-have, exclusions) with the scoring mechanics of the configuration
/// (base score, title factor, group caps, thresholds), plus the profile's skills as a small bonus (weight by level:
/// Production > Project > Learning; <c>None</c> adds nothing). Rebuilt when the profile file or the Suchprofil
/// changes. Without a Suchprofil the configured keyword lists are used.
/// </summary>
public sealed class ProfileRelevanceScorerProvider(
    RelevanceOptions options,
    ICandidateProfileSource profiles,
    ISearchProfileSource? searchProfiles = null) : IRelevanceScorerProvider
{
    private readonly Lock _lock = new();
    private (int ProfileVersion, int SearchVersion, RelevanceScorer Scorer)? _cached;

    public RelevanceScorer Scorer
    {
        get
        {
            var snapshot = profiles.Current;
            var search = searchProfiles?.Current;
            lock (_lock)
            {
                if (_cached is { } cached && cached.ProfileVersion == snapshot.Version && cached.SearchVersion == (search?.Version ?? 0))
                {
                    return cached.Scorer;
                }

                var scorer = Build(options, search?.Profile, snapshot.Usable);
                _cached = (snapshot.Version, search?.Version ?? 0, scorer);
                return scorer;
            }
        }
    }

    /// <summary>A scorer for a given Suchprofil and profile — also used by the preview without saving anything.</summary>
    public static RelevanceScorer Build(RelevanceOptions mechanics, SearchProfile? searchProfile, CandidateProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(mechanics);
        if (searchProfile is null)
        {
            return new RelevanceScorer(mechanics, RulesFrom(profile, mechanics.ProfileSkills));
        }

        var effective = new RelevanceOptions
        {
            BaseScore = mechanics.BaseScore,
            TitleFactor = mechanics.TitleFactor,
            SoftenedFactor = mechanics.SoftenedFactor,
            RelevantFrom = mechanics.RelevantFrom,
            MaybeFrom = mechanics.MaybeFrom,
            GroupCaps = mechanics.GroupCaps,
            ProfileSkills = mechanics.ProfileSkills,
            RescoreOnStartup = mechanics.RescoreOnStartup,
        };
        effective.Positive.AddRange(searchProfile.MustHave.Concat(searchProfile.NiceToHave).Select(k => k.ToRule()));
        effective.Negative.AddRange(searchProfile.Exclude.Select(k => k.ToRule()));
        return new RelevanceScorer(effective, RulesFrom(profile, mechanics.ProfileSkills));
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
