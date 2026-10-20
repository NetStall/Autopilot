using JobAutopilot.Core.Fit;
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
/// changes. Without a Suchprofil the configured keyword lists are used. A keyword that names a technology only scores
/// when the profile has it (<see cref="ForProfile"/>).
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
            NiceToHaveFactor = mechanics.NiceToHaveFactor,
            LearningKeywordFactor = mechanics.LearningKeywordFactor,
            BasicsGroup = mechanics.BasicsGroup,
        };
        effective.BasicSkills.AddRange(mechanics.BasicSkills);
        var keywords = searchProfile is null
            ? mechanics.Positive
            : searchProfile.MustHave.Concat(searchProfile.NiceToHave).Select(k => k.ToRule());
        effective.Positive.AddRange(ForProfile(keywords, profile, mechanics.LearningKeywordFactor));
        effective.Negative.AddRange(searchProfile is null ? mechanics.Negative : searchProfile.Exclude.Select(k => k.ToRule()));
        return new RelevanceScorer(effective, RulesFrom(profile, mechanics.ProfileSkills));
    }

    /// <summary>
    /// Keywords as the profile allows them: a skill at <c>Production</c>/<c>Project</c> scores fully, one I am
    /// learning with <paramref name="learningFactor"/>, one at <c>None</c> not at all. A keyword that names a known
    /// technology (<see cref="TechnologyCatalog"/>) the profile does not list does not score either — Blazor in the
    /// Suchprofil is a requirement to check, not a match. Other keywords („Fullstack", „Softwareentwicklung") stay.
    /// Without a profile every keyword stays.
    /// </summary>
    public static IEnumerable<RelevanceRule> ForProfile(IEnumerable<RelevanceRule> keywords, CandidateProfile? profile, double learningFactor)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        foreach (var keyword in keywords)
        {
            if (profile is null)
            {
                yield return keyword;
                continue;
            }

            var names = new HashSet<string>(keyword.Terms.Append(keyword.Name).Select(SkillTerms.Key), StringComparer.Ordinal);
            var skill = profile.Skills.FirstOrDefault(s => s.AllNames().Any(n => names.Contains(SkillTerms.Key(n))));
            switch (skill?.Level)
            {
                case SkillLevel.Production or SkillLevel.Project:
                    yield return keyword;
                    break;
                case SkillLevel.Learning:
                    yield return WithWeight(keyword, Math.Max(1, (int)Math.Round(keyword.Weight * learningFactor, MidpointRounding.AwayFromZero)));
                    break;
                case null when !keyword.Terms.Append(keyword.Name).Any(TechnologyCatalog.Contains):
                    yield return keyword;
                    break;
            }
        }
    }

    private static RelevanceRule WithWeight(RelevanceRule rule, int weight)
    {
        var copy = new RelevanceRule
        {
            Name = rule.Name,
            Weight = weight,
            MatchCase = rule.MatchCase,
            TitleOnly = rule.TitleOnly,
            Core = rule.Core,
            Group = rule.Group,
            SoftenedByCore = rule.SoftenedByCore,
            Reason = rule.Reason,
        };
        copy.Terms.AddRange(rule.Terms);
        return copy;
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
            // Only the name and specific aliases: „CI/CD" in a posting is no evidence for GitHub Actions.
            rule.Terms.AddRange(SkillTerms.Evidence(skill).Select(n => n.Trim()));
            yield return rule;
        }
    }
}
