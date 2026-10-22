using JobAutopilot.Core.Documents;

namespace JobAutopilot.Tests.Fakes;

/// <summary><see cref="IPackageTextGenerator"/> with fixed texts — tests never call Claude.</summary>
public sealed class FakePackageTextGenerator : IPackageTextGenerator
{
    public string EmailBody { get; set; } = "anbei sende ich Ihnen meine Bewerbung als Full-Stack-Entwickler. Anschreiben und Lebenslauf finden Sie im Anhang.";

    public int Calls { get; private set; }

    public Task<PackageGeneration> GenerateAsync(LetterInput input, LetterDraft letter, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new PackageGeneration(EmailBody, "C# und Vue sind mein Stack.", "Full-Stack-Entwickler mit C# und Vue.",
            new TokenUsage(2000, 300), "fake-model", "package.v1"));
    }
}
