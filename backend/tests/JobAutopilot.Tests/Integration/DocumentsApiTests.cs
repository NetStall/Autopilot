using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.Applications;
using JobAutopilot.Api.Features.Documents;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Documents;
using JobAutopilot.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobAutopilot.Tests.Integration;

/// <summary>Shared setup: example profile, a temp folder for PDFs and the scripted letter generator (never Claude).</summary>
public abstract class DocumentsTestBase(PostgresFixture postgres) : IntegrationTestBase(postgres), IDisposable
{
    protected const string Description =
        "Wir suchen Verstärkung für unser Team. Sie entwickeln mit C#, ASP.NET Core, Vue.js und Angular. " +
        "Kubernetes setzen wir im Betrieb ein und ist ein Muss. Wir freuen uns auf Ihre Bewerbung.";

    protected string OutputDir { get; } = Directory.CreateTempSubdirectory("documents-api-").FullName;

    protected FakeLetterGenerator Generator { get; } = new();

    protected virtual string ProfilePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "profile.example.json");

    protected virtual IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>();

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(ExtraSettings)
    {
        ["Profile:Path"] = ProfilePath,
        ["Documents:OutputPath"] = OutputDir,
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ILetterGenerator>();
        services.AddSingleton<ILetterGenerator>(Generator);
    }

    public void Dispose()
    {
        if (Directory.Exists(OutputDir))
        {
            Directory.Delete(OutputDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    protected async Task<Core.Vacancies.Vacancy> SeedVacancyAsync(string? description = Description)
    {
        var vacancy = VacancySeed.Make(title: "Full-Stack-Entwickler (m/w/d)", company: "Beispiel GmbH", location: "94469 Deggendorf");
        vacancy.Description = description;
        vacancy.DuplicateKey = Core.Vacancies.DuplicateKeys.For(vacancy.Company, vacancy.Title);
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync(CancellationToken);
        return vacancy;
    }

    protected async Task<DocumentsDto> CreateAsync(Guid vacancyId, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await Client.PostAsync($"/api/vacancies/{vacancyId}/documents", null, CancellationToken);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(CancellationToken));
        return (await response.Content.ReadFromJsonAsync<DocumentsDto>(ApiJson, CancellationToken))!;
    }

    protected Task<HttpResponseMessage> PostJsonAsync(string url, object body) =>
        Client.PostAsJsonAsync(url, body, ApiJson, CancellationToken);

    protected static async Task<DocumentsDto> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(CancellationToken));
        return (await response.Content.ReadFromJsonAsync<DocumentsDto>(ApiJson, CancellationToken))!;
    }

    protected Task<HttpResponseMessage> SaveAsync(DocumentsDto documents, Func<LetterDto, LetterDto>? change = null, CvOptionsDto? cv = null) =>
        Client.PutAsJsonAsync($"/api/applications/{documents.ApplicationId}/documents",
            new { version = documents.Version, letter = change is null ? documents.Letter : change(documents.Letter), cvOptions = cv ?? documents.CvOptions },
            ApiJson, CancellationToken);

    protected static LetterDto WithParagraph(LetterDto letter, int index, string text)
    {
        var paragraphs = letter.Paragraphs.ToList();
        paragraphs[index] = text;
        return new LetterDto
        {
            Subject = letter.Subject, Salutation = letter.Salutation, Paragraphs = paragraphs, Closing = letter.Closing,
            Formality = letter.Formality, Language = letter.Language, FactsUsed = letter.FactsUsed, GapsMentioned = letter.GapsMentioned,
        };
    }
}

public class DocumentsApiTests(PostgresFixture postgres) : DocumentsTestBase(postgres)
{
    [Fact]
    public async Task Creating_documents_for_a_vacancy_makes_a_draft_application_with_letter_cv_and_cheat_sheet()
    {
        var vacancy = await SeedVacancyAsync();

        var documents = await CreateAsync(vacancy.Id);

        documents.Status.Should().Be(ApplicationStatus.Draft);
        documents.StatusLabel.Should().Be("Entwurf");
        documents.VacancyId.Should().Be(vacancy.Id);
        documents.Letter.Paragraphs.Should().HaveCount(4);
        documents.Warnings.Should().BeEmpty();
        documents.LetterPages.Should().Be(1);
        documents.CvPages.Should().Be(1);
        documents.Cv.Focus.Should().Be("fullstack");
        documents.Cv.Language.Should().Be(DocumentLanguage.De);
        documents.Usage.Should().Match<DraftUsageDto>(u => u.InputTokens == 3000 && u.OutputTokens == 900 && u.Calls == 1 && u.CostUsd > 0);
        documents.PromptVersion.Should().Be("letter.v1");
        documents.Revisions.Should().ContainSingle().Which.Kind.Should().Be(DraftRevisionKind.Generated);
        documents.CheatSheet.Should().Contain(i => i.Key == "subject" && i.Value == "Bewerbung als Full-Stack-Entwickler (m/w/d)");
        documents.CheatSheet.Should().Contain(i => i.Key == "foundVia" && i.Value == "Jobbörse der Bundesagentur für Arbeit");
        Generator.Inputs.Single().Vacancy.Description.Should().Be(Description);

        var again = await CreateAsync(vacancy.Id, HttpStatusCode.OK);
        again.ApplicationId.Should().Be(documents.ApplicationId);
        Generator.Calls.Should().Be(1, "existing documents are returned, not generated again");

        var applications = await Client.GetFromJsonAsync<List<ApplicationDto>>("/api/applications", ApiJson, CancellationToken);
        applications!.Single().Status.Should().Be(ApplicationStatus.Draft);
    }

    [Fact]
    public async Task A_letter_mixing_du_and_sie_is_generated_once_more_with_the_warning_as_feedback()
    {
        // SOS case (08a): the first attempt mixes the forms of address; the retry gets the warning and fixes it.
        var vacancy = await SeedVacancyAsync();
        Generator.Respond = input =>
        {
            var clean = FakeLetterGenerator.CleanLetter(input);
            return Generator.Calls == 1 ? clean with { Paragraphs = [.. clean.Paragraphs.Take(3), "Meine Deutschkenntnisse liegen bei B2. Ich freue mich auf dich."] } : clean;
        };

        var documents = await CreateAsync(vacancy.Id);

        Generator.Calls.Should().Be(2);
        Generator.Feedback[1].Should().Contain("gemischt");
        documents.Warnings.Should().NotContain(w => w.Code == DraftWarning.MixedFormality);
        documents.Revisions.Should().HaveCount(2).And.Contain(r => r.Note != null && r.Note.StartsWith("verworfen", StringComparison.Ordinal));
        documents.Usage.Calls.Should().Be(2, "both attempts count against the daily limit");
    }

    [Theory]
    [InlineData(LetterGenerationError.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(LetterGenerationError.NotConfigured, HttpStatusCode.ServiceUnavailable)]
    [InlineData(LetterGenerationError.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData(LetterGenerationError.InvalidResponse, HttpStatusCode.BadGateway)]
    public async Task A_failed_generation_stores_nothing(LetterGenerationError error, HttpStatusCode status)
    {
        var vacancy = await SeedVacancyAsync();
        Generator.Failure = new LetterGenerationException(error, "Fehler beim Erstellen.");

        var response = await Client.PostAsync($"/api/vacancies/{vacancy.Id}/documents", null, CancellationToken);

        response.StatusCode.Should().Be(status);
        (await response.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("Fehler beim Erstellen.").And.Contain(error.ToString());
        await using var db = Postgres.CreateDbContext();
        (await db.Applications.CountAsync(CancellationToken)).Should().Be(0);
        (await db.ApplicationDrafts.CountAsync(CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Saving_checks_the_version_and_runs_the_fact_guard_again()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);

        var saved = await ReadAsync(await SaveAsync(documents, l => WithParagraph(l, 1, "Ich habe ein Team von 12 Entwicklern geleitet.")));

        saved.Version.Should().Be(documents.Version + 1);
        saved.Letter.Paragraphs[1].Should().Be("Ich habe ein Team von 12 Entwicklern geleitet.");
        saved.Warnings.Should().ContainSingle(w => w.Code == DraftWarning.InventedNumber).Which.Paragraph.Should().Be(1);
        saved.HasOpenWarnings.Should().BeTrue();
        saved.Revisions.Select(r => r.Kind).Should().Equal(DraftRevisionKind.Saved, DraftRevisionKind.Generated);

        var stale = await SaveAsync(documents);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Rephrasing_changes_only_that_paragraph_and_adds_the_tokens()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);

        var result = await ReadAsync(await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/paragraphs/1/rephrase",
            new { version = documents.Version, hint = "kürzer" }));

        result.Letter.Paragraphs[1].Should().Be(Generator.RephrasedText);
        result.Letter.Paragraphs.Where((_, i) => i != 1).Should().Equal(documents.Letter.Paragraphs.Where((_, i) => i != 1));
        result.Usage.Calls.Should().Be(2);
        result.Usage.InputTokens.Should().Be(6000);
        result.Revisions[0].Should().Match<DraftRevisionDto>(r => r.Kind == DraftRevisionKind.Rephrased && r.Note == "Absatz 2");

        var missing = await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/paragraphs/9/rephrase", new { version = result.Version });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approval_with_open_warnings_needs_confirmation_and_writes_the_pdfs()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);
        documents = await ReadAsync(await SaveAsync(documents, l => WithParagraph(l, 1, "Ich habe Kubernetes-Cluster betrieben.")));
        documents.HasOpenWarnings.Should().BeTrue();
        (await Client.GetAsync($"/api/applications/{documents.ApplicationId}/documents/letter.pdf", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "PDFs exist only after approval");

        var refused = await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/approve", new { version = documents.Version });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("ausdrücklich bestätigen");

        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var approved = await ReadAsync(await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/approve",
            new { version = documents.Version, acceptWarnings = true }));

        approved.Status.Should().Be(ApplicationStatus.Approved);
        approved.WarningsAccepted.Should().BeTrue();
        approved.LetterFile.Should().Be("Alex_Beispiel_Anschreiben_Beispiel_GmbH.pdf");
        approved.CvFile.Should().Be("Alex_Beispiel_Lebenslauf_Beispiel_GmbH.pdf");
        File.Exists(Path.Combine(OutputDir, documents.ApplicationId.ToString(), approved.LetterFile!)).Should().BeTrue();

        var pdf = await Client.GetAsync($"/api/applications/{documents.ApplicationId}/documents/letter.pdf", CancellationToken);
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        pdf.Content.Headers.ContentDisposition!.FileName.Should().Contain("Alex_Beispiel_Anschreiben_Beispiel_GmbH.pdf");
        (await pdf.Content.ReadAsByteArrayAsync(CancellationToken)).Take(5).Should().Equal("%PDF-"u8.ToArray());

        var application = await Client.GetFromJsonAsync<ApplicationDto>($"/api/applications/{documents.ApplicationId}", ApiJson, CancellationToken);
        application!.Documents.Should().Equal(approved.LetterFile, approved.CvFile);
        application.Events![0].Note.Should().Contain("bestätigten Warnungen");
    }

    [Fact]
    public async Task Sent_is_reached_only_through_mark_sent_after_approval()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);
        var url = $"/api/applications/{documents.ApplicationId}";

        (await PostJsonAsync($"{url}/mark-sent", new { channel = "E-Mail" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostJsonAsync($"{url}/status", new { status = "Sent" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostJsonAsync($"{url}/status", new { status = "Approved" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostJsonAsync($"{url}/status", new { status = "Interview" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await ReadAsync(await PostJsonAsync($"{url}/documents/approve", new { version = documents.Version }));
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var sent = await ReadAsync(await PostJsonAsync($"{url}/mark-sent", new { channel = "Bewerbungsportal" }));

        sent.Status.Should().Be(ApplicationStatus.Sent);
        var application = await Client.GetFromJsonAsync<ApplicationDto>(url, ApiJson, CancellationToken);
        application!.Channel.Should().Be("Bewerbungsportal");
        application.AppliedAt.Should().Be(Factory.Clock.GetUtcNow());
        application.FollowUpAt.Should().Be(Factory.Clock.GetUtcNow().AddDays(7));
        application.Events!.Select(e => e.To).Should().Equal(ApplicationStatus.Sent, ApplicationStatus.Approved, ApplicationStatus.Draft);

        (await SaveAsync(sent)).StatusCode.Should().Be(HttpStatusCode.Conflict, "sent documents are no longer editable");
        (await PostJsonAsync($"{url}/status", new { status = "Interview" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_change_after_approval_needs_a_new_approval()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);
        var approved = await ReadAsync(await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/approve", new { version = documents.Version }));

        var changed = await ReadAsync(await SaveAsync(approved, l => WithParagraph(l, 3, "Meine Deutschkenntnisse liegen bei B2. Ich freue mich auf Ihre Rückmeldung.")));

        changed.Status.Should().Be(ApplicationStatus.Draft);
        changed.ApprovedAt.Should().BeNull();
        (await PostJsonAsync($"/api/applications/{documents.ApplicationId}/mark-sent", new { channel = "E-Mail" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_draft_can_be_withdrawn()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);

        var response = await PostJsonAsync($"/api/applications/{documents.ApplicationId}/status", new { status = "Withdrawn" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cv_choices_are_saved_and_older_revisions_can_be_restored()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);
        var cv = new CvOptionsDto { Focus = "backend", HiddenBullets = ["exp0.b2"], HiddenSkillGroups = ["Frontend"] };

        var saved = await ReadAsync(await SaveAsync(documents, l => WithParagraph(l, 0, "Geändert, mehr auf example.org."), cv));
        saved.Cv.Focus.Should().Be("backend");
        saved.Cv.Experience.Single().Bullets.Single(b => b.Id == "exp0.b2").Hidden.Should().BeTrue();
        saved.Cv.SkillGroups.Single(g => g.Name == "Frontend").Hidden.Should().BeTrue();

        var restored = await ReadAsync(await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/restore",
            new { version = saved.Version, revision = 1 }));

        restored.Letter.Paragraphs[0].Should().Be(documents.Letter.Paragraphs[0]);
        restored.Cv.Focus.Should().Be("fullstack");
        restored.Revisions.Select(r => r.Kind).Should().Equal(DraftRevisionKind.Restored, DraftRevisionKind.Saved, DraftRevisionKind.Generated);
    }

    [Fact]
    public async Task Preview_renders_the_unsaved_state_as_png()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);

        var response = await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/preview/letter",
            new { letter = WithParagraph(documents.Letter, 0, "Ungespeicherter Text.") });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        response.Headers.GetValues("X-Page-Count").Should().Equal("1");
        (await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/preview/cv", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/preview/other", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var current = await Client.GetFromJsonAsync<DocumentsDto>($"/api/applications/{documents.ApplicationId}/documents", ApiJson, CancellationToken);
        current!.Version.Should().Be(documents.Version, "a preview saves nothing");
    }

    [Fact]
    public async Task Invalid_letters_are_rejected()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);

        var empty = await SaveAsync(documents, l => new LetterDto { Subject = l.Subject, Salutation = l.Salutation, Closing = l.Closing, Paragraphs = ["  "] });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var tooMany = await SaveAsync(documents, l => new LetterDto { Subject = l.Subject, Salutation = l.Salutation, Closing = l.Closing, Paragraphs = [.. Enumerable.Repeat("x", 9)] });
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deleting_the_application_deletes_documents_and_pdfs()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);
        await ReadAsync(await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/approve", new { version = documents.Version }));
        Directory.Exists(Path.Combine(OutputDir, documents.ApplicationId.ToString())).Should().BeTrue();

        (await Client.DeleteAsync($"/api/applications/{documents.ApplicationId}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Directory.Exists(Path.Combine(OutputDir, documents.ApplicationId.ToString())).Should().BeFalse();
        await using var db = Postgres.CreateDbContext();
        (await db.ApplicationDrafts.CountAsync(CancellationToken)).Should().Be(0);
        (await db.ApplicationDraftRevisions.CountAsync(CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Documents_for_a_sent_application_are_not_created()
    {
        var vacancy = await SeedVacancyAsync();
        (await Client.PostAsync($"/api/vacancies/{vacancy.Id}/apply", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Client.PostAsync($"/api/vacancies/{vacancy.Id}/documents", null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Generator.Calls.Should().Be(0);
    }
}

public class DocumentLimitsApiTests(PostgresFixture postgres) : DocumentsTestBase(postgres)
{
    protected override IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>
    {
        ["Claude:DailyCallLimit"] = "2",
        ["Claude:DraftTokenBudget"] = "5000",
    };

    [Fact]
    public async Task The_token_budget_of_a_draft_stops_further_calls()
    {
        var documents = await CreateAsync((await SeedVacancyAsync()).Id);
        Generator.Usage = new TokenUsage(4000, 1000);
        var regenerated = await ReadAsync(await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/regenerate", new { version = documents.Version }));
        regenerated.Usage.InputTokens.Should().Be(7000);

        var refused = await PostJsonAsync($"/api/applications/{documents.ApplicationId}/documents/paragraphs/0/rephrase", new { version = regenerated.Version });

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await refused.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("Token-Budget");
        Generator.Calls.Should().Be(2);
    }

    [Fact]
    public async Task The_daily_call_limit_counts_all_drafts()
    {
        await CreateAsync((await SeedVacancyAsync()).Id);
        await CreateAsync((await SeedVacancyAsync()).Id);

        var third = await Client.PostAsync($"/api/vacancies/{(await SeedVacancyAsync()).Id}/documents", null, CancellationToken);

        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await third.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("Tageslimit");
        Generator.Calls.Should().Be(2);

        Factory.Clock.Advance(TimeSpan.FromDays(1));
        (await Client.PostAsync($"/api/vacancies/{(await SeedVacancyAsync()).Id}/documents", null, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created, "the limit resets at midnight");
    }
}

public class DocumentsWithoutProfileApiTests(PostgresFixture postgres) : DocumentsTestBase(postgres)
{
    protected override string ProfilePath => Path.Combine(AppContext.BaseDirectory, "does-not-exist", "profile.json");

    [Fact]
    public async Task Without_a_profile_there_are_no_documents()
    {
        var response = await Client.PostAsync($"/api/vacancies/{(await SeedVacancyAsync()).Id}/documents", null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Generator.Calls.Should().Be(0);
    }
}
