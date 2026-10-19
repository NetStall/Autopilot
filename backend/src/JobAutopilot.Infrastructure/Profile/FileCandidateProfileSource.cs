using System.Text.Json;
using System.Text.Json.Serialization;
using JobAutopilot.Core.Profile;
using Microsoft.Extensions.Logging;

namespace JobAutopilot.Infrastructure.Profile;

/// <summary>Configuration section <c>Profile</c>.</summary>
public sealed class ProfileOptions
{
    public const string SectionName = "Profile";

    /// <summary>Path of the profile JSON; relative paths are resolved against the API's content root.</summary>
    public string Path { get; set; } = "data/profile.json";
}

/// <summary>
/// Reads the candidate profile from a JSON file and re-reads it when the file changes (checked at most every
/// <see cref="CheckInterval"/>, on access) — no restart needed. A missing or invalid file gives a snapshot with clear
/// errors instead of an exception, so the rest of the API keeps working. The content is personal data: it is never
/// logged, only the file name, status and the number of problems.
/// </summary>
public sealed partial class FileCandidateProfileSource(string fullPath, TimeProvider timeProvider, ILogger<FileCandidateProfileSource> logger)
    : ICandidateProfileSource
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private readonly Lock _lock = new();
    private ProfileSnapshot? _snapshot;
    private (DateTime WriteTime, long Length)? _fileStamp;
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
    private int _version;

    public string FullPath { get; } = fullPath;

    public ProfileSnapshot Current
    {
        get
        {
            lock (_lock)
            {
                var now = timeProvider.GetUtcNow();
                if (_snapshot is not null && now - _lastCheck < CheckInterval)
                {
                    return _snapshot;
                }

                _lastCheck = now;
                var stamp = Stamp();
                if (_snapshot is null || stamp != _fileStamp)
                {
                    _fileStamp = stamp;
                    _snapshot = Load(stamp is not null, now, ++_version);
                    LogLoaded(logger, _snapshot.FileName, _snapshot.Status, _snapshot.Errors.Count, _snapshot.Warnings.Count);
                }

                return _snapshot;
            }
        }
    }

    /// <summary>Parses and validates a profile JSON text (also used by tests).</summary>
    public static (CandidateProfile? Profile, ProfileValidation Validation) Parse(string json, DateOnly today)
    {
        CandidateProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<CandidateProfile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            var where = ex.Path is { Length: > 0 } path ? $"{path} (Zeile {(ex.LineNumber ?? 0) + 1})" : $"Zeile {(ex.LineNumber ?? 0) + 1}";
            return (null, new ProfileValidation([$"Ungültiges JSON bei {where}: {FirstSentence(ex.Message)}"], []));
        }

        if (profile is null)
        {
            return (null, new ProfileValidation(["Die Datei ist leer."], []));
        }

        return (profile, ProfileValidator.Validate(profile, today));
    }

    private ProfileSnapshot Load(bool exists, DateTimeOffset now, int version)
    {
        var fileName = Path.GetFileName(FullPath);
        if (!exists)
        {
            return new ProfileSnapshot(ProfileStatus.Missing, null,
                [$"Profil-Datei nicht gefunden: {fileName}. Vorlage: docs/profile.example.json (siehe README, „Profil“)."],
                [], fileName, now, version);
        }

        string json;
        try
        {
            json = File.ReadAllText(FullPath);
        }
        catch (IOException ex)
        {
            return new ProfileSnapshot(ProfileStatus.Invalid, null, [$"Profil-Datei nicht lesbar: {ex.Message}"], [], fileName, now, version);
        }

        var (profile, validation) = Parse(json, DateOnly.FromDateTime(now.UtcDateTime));
        var status = profile is not null && validation.IsValid ? ProfileStatus.Loaded : ProfileStatus.Invalid;
        return new ProfileSnapshot(status, profile, validation.Errors, validation.Warnings, fileName, now, version);
    }

    private (DateTime, long)? Stamp()
    {
        var info = new FileInfo(FullPath);
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
    }

    private static string FirstSentence(string message)
    {
        var end = message.IndexOf(". ", StringComparison.Ordinal);
        return end > 0 ? message[..(end + 1)] : message;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Profile {FileName}: {Status}, {Errors} error(s), {Warnings} warning(s)")]
    private static partial void LogLoaded(ILogger logger, string fileName, ProfileStatus status, int errors, int warnings);
}

/// <summary>Used where no profile file is configured (e.g. infrastructure tests): always „missing".</summary>
public sealed class NoCandidateProfileSource : ICandidateProfileSource
{
    public ProfileSnapshot Current { get; } =
        new(ProfileStatus.Missing, null, ["Kein Profil konfiguriert."], [], string.Empty, null, 0);
}
