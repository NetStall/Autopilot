using System.Text.Json;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Llm;
using JobAutopilot.Infrastructure.Claude;
using JobAutopilot.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Documents;

/// <summary>
/// <see cref="IPackageTextGenerator"/> with the letter model (<c>Llm:Tasks:Letter</c>) and the letter prompt's system
/// rules (facts only, the posting is data). Texts are trimmed to their maximum length; nothing is sent anywhere else.
/// </summary>
public sealed class PackageTextGenerator(ILlmRouter llm, IOptions<ClaudeOptions> options) : IPackageTextGenerator
{
    public const string PromptVersion = "package.v1";

    public async Task<PackageGeneration> GenerateAsync(LetterInput input, LetterDraft letter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var prompt = LetterPrompt.For(options.Value.LetterPromptVersion);
        LlmResponse response;
        try
        {
            response = await llm.CompleteJsonAsync(
                new LlmRequest(LlmOptions.LetterTask, "package", PromptVersion, prompt.System,
                    prompt.PackageMessage(input, letter, options.Value.MaxVacancyCharacters), LetterPrompt.PackageSchema),
                cancellationToken);
        }
        catch (LlmException ex)
        {
            throw new LetterGenerationException(LetterGenerator.ToLetterError(ex.Error), ex.Message, ex);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Json);
            string Text(string name, int max)
            {
                var value = (document.RootElement.GetProperty(name).GetString() ?? string.Empty).Trim();
                return value.Length > max ? value[..max].TrimEnd() : value;
            }

            return new PackageGeneration(
                Text("emailBody", PackageTexts.MaxEmailBody), Text("motivation", PackageTexts.MaxMotivation), Text("aboutMe", PackageTexts.MaxAboutMe),
                response.Usage, response.Model, PromptVersion);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new LetterGenerationException(LetterGenerationError.InvalidResponse, "Das Bewerbungspaket hat nicht das erwartete Format.", ex);
        }
    }
}
