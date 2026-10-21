using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Claude;

/// <summary>
/// Configuration section <c>Claude</c>. The API key comes only from user-secrets / environment
/// (<c>Claude__ApiKey</c>) — never from appsettings, never sent to the frontend, never logged.
/// </summary>
public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";

    public static readonly string[] Efforts = ["low", "medium", "high", "xhigh", "max"];

    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>Upper limit of output tokens per call (thinking included).</summary>
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Only sent when set. Current models (Sonnet 5.5, Opus 5.5) reject a non-default temperature with 400 — leave it
    /// empty for them; older models accept 0–1.
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>Thinking effort (<c>low</c> … <c>max</c>); empty = the model's default.</summary>
    public string? Effort { get; set; } = "medium";

    /// <summary>Timeout of one HTTP call (retries not included).</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>Retries on 429, 5xx and connection errors, with backoff (done by the SDK).</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Model calls (generations + rephrasings) per day, counted in German time.</summary>
    public int DailyCallLimit { get; set; } = 30;

    /// <summary>Total tokens (input + output) all calls of one draft may use.</summary>
    public int DraftTokenBudget { get; set; } = 120_000;

    /// <summary>USD per million tokens — only for the cost estimate in the UI.</summary>
    public decimal InputPricePerMTok { get; set; } = 2m;

    public decimal OutputPricePerMTok { get; set; } = 10m;

    /// <summary>Longer posting texts are cut (cost; the end of a posting is usually boilerplate).</summary>
    public int MaxVacancyCharacters { get; set; } = 12_000;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public decimal EstimateCostUsd(int inputTokens, int outputTokens) =>
        Math.Round(((inputTokens * InputPricePerMTok) + (outputTokens * OutputPricePerMTok)) / 1_000_000m, 4);
}

public sealed class ClaudeOptionsValidator : IValidateOptions<ClaudeOptions>
{
    public ValidateOptionsResult Validate(string? name, ClaudeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Claude:BaseUrl must be an absolute https URL.");
        }

        if (options.MaxTokens is < 256 or > 64_000)
        {
            errors.Add("Claude:MaxTokens must be between 256 and 64000.");
        }

        if (options.Temperature is < 0 or > 1)
        {
            errors.Add("Claude:Temperature must be between 0 and 1 (or empty).");
        }

        if (!string.IsNullOrWhiteSpace(options.Effort) && !ClaudeOptions.Efforts.Contains(options.Effort))
        {
            errors.Add($"Claude:Effort must be one of {string.Join(", ", ClaudeOptions.Efforts)} (or empty).");
        }

        if (options.TimeoutSeconds is < 5 or > 600 || options.MaxRetries is < 0 or > 5)
        {
            errors.Add("Claude:TimeoutSeconds must be 5–600 and Claude:MaxRetries 0–5.");
        }

        if (options.DailyCallLimit < 1 || options.DraftTokenBudget < 1000 || options.MaxVacancyCharacters < 1000)
        {
            errors.Add("Claude:DailyCallLimit ≥ 1, Claude:DraftTokenBudget ≥ 1000, Claude:MaxVacancyCharacters ≥ 1000.");
        }

        if (options.InputPricePerMTok < 0 || options.OutputPricePerMTok < 0)
        {
            errors.Add("Claude prices must not be negative.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
