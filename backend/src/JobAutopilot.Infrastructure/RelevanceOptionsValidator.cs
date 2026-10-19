using JobAutopilot.Core.Scoring;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure;

/// <summary>Fails the start of the app with a clear message when the relevance rules are invalid.</summary>
internal sealed class RelevanceOptionsValidator : IValidateOptions<RelevanceOptions>
{
    public ValidateOptionsResult Validate(string? name, RelevanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = options.Validate();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
