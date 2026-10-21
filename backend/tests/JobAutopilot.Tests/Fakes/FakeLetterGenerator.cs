using JobAutopilot.Core.Documents;

namespace JobAutopilot.Tests.Fakes;

/// <summary>
/// A scripted <see cref="ILetterGenerator"/>: returns <see cref="Letter"/> (or what <see cref="Respond"/> builds),
/// or throws <see cref="Failure"/>. Records every input, so tests can check what would have been sent.
/// </summary>
public sealed class FakeLetterGenerator : ILetterGenerator
{
    public static readonly TokenUsage DefaultUsage = new(3000, 900);

    public string PromptVersion => "letter.v1";

    public Func<LetterInput, LetterDraft>? Respond { get; set; }

    public LetterGenerationException? Failure { get; set; }

    public TokenUsage Usage { get; set; } = DefaultUsage;

    public string RephrasedText { get; set; } = "Neu formulierter Absatz mit C# und Vue.";

    public List<LetterInput> Inputs { get; } = [];

    public int Calls { get; private set; }

    public List<string?> Feedback { get; } = [];

    public Task<LetterGeneration> GenerateAsync(LetterInput input, CancellationToken cancellationToken, string? feedback = null)
    {
        Calls++;
        Inputs.Add(input);
        Feedback.Add(feedback);
        if (Failure is not null)
        {
            throw Failure;
        }

        var letter = Respond?.Invoke(input) ?? CleanLetter(input);
        return Task.FromResult(new LetterGeneration(letter, Usage, "fake-model", PromptVersion));
    }

    public Task<ParagraphRephrasing> RephraseParagraphAsync(
        LetterInput input, LetterDraft current, int paragraph, string? hint, CancellationToken cancellationToken)
    {
        Calls++;
        Inputs.Add(input);
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult(new ParagraphRephrasing(RephrasedText, ["exp0.b0"], [], Usage, "fake-model", PromptVersion));
    }

    /// <summary>A letter that uses only facts of the example profile (no warnings for the standard test posting).</summary>
    public static LetterDraft CleanLetter(LetterInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new LetterDraft(
            $"Bewerbung als {input.Vacancy.Title}",
            input.Tone.Salutation,
            [
                "Ihre Stelle verbindet C#/.NET und Vue – genau mein Stack, den ich seit 4 Jahren beruflich einsetze; mehr dazu auf example.org.",
                "Bei der Muster Software GmbH habe ich REST-APIs mit ASP.NET Core für drei Client-Anwendungen entwickelt und das Frontend von jQuery zu Vue 3 migriert.",
                "Ganz offen: Mit Angular und Kubernetes habe ich noch nicht gearbeitet, bringe aber 3 Jahre Vue 3 und TypeScript mit.",
                "Meine Deutschkenntnisse liegen bei B2. Ich freue mich auf ein Gespräch.",
            ],
            "Mit freundlichen Grüßen",
            DocumentLanguage.De,
            Formality.Sie,
            ["personal.website", "exp0.b0"],
            ["Angular", "Kubernetes"]);
    }
}
