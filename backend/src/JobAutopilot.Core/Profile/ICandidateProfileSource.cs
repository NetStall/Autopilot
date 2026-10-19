namespace JobAutopilot.Core.Profile;

public enum ProfileStatus
{
    Loaded,
    Missing,
    Invalid,
}

/// <summary>The profile as currently loaded. <see cref="Version"/> changes whenever the file was re-read.</summary>
public sealed record ProfileSnapshot(
    ProfileStatus Status,
    CandidateProfile? Profile,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string FileName,
    DateTimeOffset? LoadedAt,
    int Version)
{
    /// <summary>The usable profile, or <c>null</c> when it is missing or invalid.</summary>
    public CandidateProfile? Usable => Status == ProfileStatus.Loaded ? Profile : null;
}

/// <summary>Gives the current candidate profile; implementations re-read it when the file changes.</summary>
public interface ICandidateProfileSource
{
    ProfileSnapshot Current { get; }
}
