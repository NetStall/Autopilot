using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace JobAutopilot.Core.Profile;

/// <summary>
/// One English profile text without its German field: <see cref="Path"/> of the source value (<c>experience[0].bullets[2].text</c>),
/// <see cref="Target"/> the German property to add next to it (<c>textDe</c>, or <c>fullstack.de</c> in <c>summary</c>).
/// </summary>
public sealed record MissingTranslation(string Path, string Target, string Source);

/// <summary>A German text for one <see cref="MissingTranslation.Path"/> — drafted by the model, approved by me.</summary>
public sealed record ProfileTranslation(string Path, string German);

/// <summary>
/// „Fehlende deutsche Texte übersetzen" (06c): which texts the German CV takes over in English (summary per focus,
/// job titles, bullets, degrees), and inserting approved German texts into the profile JSON <b>as text</b> — right
/// after the source value, so comments, order and formatting of my file stay as they are. Nothing is written here;
/// the caller shows the result and saves it only after my approval.
/// </summary>
public static class ProfileTranslations
{
    private static readonly JsonSerializerOptions Escape = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static IReadOnlyList<MissingTranslation> Missing(CandidateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var missing = new List<MissingTranslation>();
        foreach (var (key, text) in profile.Summary.Where(s => !s.Key.EndsWith(".de", StringComparison.OrdinalIgnoreCase)))
        {
            if (!profile.Summary.ContainsKey($"{key}.de") && !string.IsNullOrWhiteSpace(text))
            {
                missing.Add(new($"summary.{key}", $"{key}.de", text));
            }
        }

        foreach (var (entry, i) in profile.Experience.Select((e, i) => (e, i)))
        {
            if (entry.TitleDe is null && !string.IsNullOrWhiteSpace(entry.Title))
            {
                missing.Add(new(Invariant($"experience[{i}].title"), "titleDe", entry.Title));
            }

            foreach (var (bullet, j) in entry.Bullets.Select((b, j) => (b, j)))
            {
                if (bullet.TextDe is null && !string.IsNullOrWhiteSpace(bullet.Text))
                {
                    missing.Add(new(Invariant($"experience[{i}].bullets[{j}].text"), "textDe", bullet.Text));
                }
            }
        }

        foreach (var (education, i) in profile.Education.Select((e, i) => (e, i)))
        {
            if (education.DegreeDe is null && !string.IsNullOrWhiteSpace(education.Degree))
            {
                missing.Add(new(Invariant($"education[{i}].degree"), "degreeDe", education.Degree));
            }
        }

        return missing;
    }

    /// <summary>
    /// The profile JSON with <c>, "textDe": "…"</c> inserted after each approved source value. Only paths that are
    /// still missing are used; the result is parsed again before it is returned.
    /// </summary>
    /// <exception cref="JsonException">The file is no valid JSON (before or after).</exception>
    public static string Insert(string json, IReadOnlyList<MissingTranslation> missing, IEnumerable<ProfileTranslation> approved)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentNullException.ThrowIfNull(approved);
        var byPath = missing.ToDictionary(m => m.Path, StringComparer.OrdinalIgnoreCase);
        var german = approved
            .Where(t => byPath.ContainsKey(t.Path) && !string.IsNullOrWhiteSpace(t.German))
            .GroupBy(t => t.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().German.Trim(), StringComparer.Ordinal);
        if (german.Count == 0)
        {
            return json;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        var inserts = new List<(int Offset, string Text)>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var path = new List<(string? Property, int Index, bool Array)>();
        string? property = null;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = reader.GetString();
                    break;
                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    Advance(path, ref property);
                    path.Add((null, -1, reader.TokenType == JsonTokenType.StartArray));
                    break;
                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    path.RemoveAt(path.Count - 1);
                    property = null;
                    break;
                default:
                    var at = Advance(path, ref property);
                    if (reader.TokenType == JsonTokenType.String && german.TryGetValue(at, out var text))
                    {
                        var target = JsonSerializer.Serialize(byPath[at].Target, Escape);
                        inserts.Add(((int)reader.BytesConsumed, $", {target}: {JsonSerializer.Serialize(text, Escape)}"));
                    }

                    break;
            }
        }

        var result = new StringBuilder();
        var position = 0;
        foreach (var (offset, text) in inserts.OrderBy(i => i.Offset))
        {
            result.Append(Encoding.UTF8.GetString(bytes, position, offset - position)).Append(text);
            position = offset;
        }

        result.Append(Encoding.UTF8.GetString(bytes, position, bytes.Length - position));
        var updated = result.ToString();
        using (JsonDocument.Parse(updated, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
        {
            return updated;
        }
    }

    /// <summary>The path of the value that starts now: <c>experience[0].bullets[2].text</c>; moves the array index on.</summary>
    private static string Advance(List<(string? Property, int Index, bool Array)> path, ref string? property)
    {
        if (path.Count > 0)
        {
            var top = path[^1];
            path[^1] = top.Array ? top with { Index = top.Index + 1 } : top with { Property = property };
        }

        property = null;
        var text = new StringBuilder();
        foreach (var (name, index, array) in path)
        {
            if (array)
            {
                text.Append(CultureInfo.InvariantCulture, $"[{index}]");
            }
            else if (name is not null)
            {
                text.Append(text.Length == 0 ? name : "." + name);
            }
        }

        return text.ToString();
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
