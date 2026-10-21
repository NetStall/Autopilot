using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.Profile;
using JobAutopilot.Core.Llm;
using JobAutopilot.Core.Profile;
using JobAutopilot.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobAutopilot.Tests.Integration;

/// <summary>„Fehlende deutsche Texte übersetzen" (06c): drafts with a scripted model, inserts only approved texts.</summary>
public class ProfileTranslationApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static readonly string ProfilePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "profiles", "regression-profile.json");

    private readonly FakeLlmClient _claude = new("claude");

    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Profile:Path"] = ProfilePath,
        ["Llm:Tasks:Letter"] = "claude:claude-sonnet-5-5",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ILlmClient>();
        services.AddSingleton<ILlmClient>(_claude);
    }

    [Fact]
    public async Task Drafts_only_the_missing_paths_and_inserts_only_what_I_approved()
    {
        var missing = await Client.GetFromJsonAsync<List<MissingTranslation>>("/api/profile/translations", CancellationToken);
        missing.Should().NotBeEmpty();
        var first = missing![0];
        _claude.Answer = _ => $$"""
            {"translations":[{"path":"{{first.Path}}","german":"Deutscher Text"},{"path":"invented.path","german":"x"}]}
            """;

        var draft = await (await Client.PostAsync("/api/profile/translations", null, CancellationToken)).Content.ReadFromJsonAsync<TranslationDraftDto>(CancellationToken);

        draft!.Suggestions.Should().ContainSingle().Which.Path.Should().Be(first.Path, "a path the model made up is dropped");
        _claude.Requests.Single().User.Should().Contain("<texts>").And.NotContain("@", "no contact data in the request");

        var json = await File.ReadAllTextAsync(ProfilePath, CancellationToken);
        var apply = await Client.PostAsJsonAsync("/api/profile/translations/apply",
            new ApplyTranslationsRequest(json, [new(first.Path, "Deutscher Text")]), CancellationToken);
        var result = await apply.Content.ReadFromJsonAsync<ApplyTranslationsDto>(CancellationToken);

        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Inserted.Should().Be(1);
        result.Json.Should().Contain($"\"{first.Target}\": \"Deutscher Text\"");
        (await File.ReadAllTextAsync(ProfilePath, CancellationToken)).Should().Be(json, "the API never writes the profile");
    }

    [Fact]
    public async Task Rejects_a_file_that_is_no_json()
    {
        var response = await Client.PostAsJsonAsync("/api/profile/translations/apply", new ApplyTranslationsRequest("{ nope", []), CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
