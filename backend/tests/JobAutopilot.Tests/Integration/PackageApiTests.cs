using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.Packages;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Documents;

namespace JobAutopilot.Tests.Integration;

/// <summary>„Bewerbungspaket" and „Heute bereit" (08c) with fake Claude clients — nothing is ever sent.</summary>
public class PackageApiTests(PostgresFixture postgres) : DocumentsTestBase(postgres)
{
    private static readonly string[] Attachments = ["Alex_Beispiel_Anschreiben_Beispiel_GmbH.pdf", "Alex_Beispiel_Lebenslauf_Beispiel_GmbH.pdf"];

    private const string EmailPosting =
        "Wir suchen Verstärkung für unser Team. Sie entwickeln mit C#, ASP.NET Core und Vue.js. Bitte senden Sie Ihre Bewerbung per E-Mail an "
        + "jobs@beispiel.de. Ihre Ansprechpartnerin: Frau Anna Müller. Bitte nennen Sie Ihre Gehaltsvorstellung.";

    private const string PortalPosting =
        "Wir suchen Verstärkung. Sie entwickeln mit C# und Vue.js. Bewerbungen per E-Mail werden nicht berücksichtigt – bitte nur über unser "
        + "Karriereportal. Bitte legen Sie Zeugnisse bei und nennen Sie Ihre Gehaltsvorstellung.";

    [Fact]
    public async Task An_email_package_has_recipient_subject_salutation_body_and_attachment_names()
    {
        var vacancy = await SeedVacancyAsync(EmailPosting);

        var package = await CreatePackageAsync(vacancy.Id);

        package.HowToApply.Channel.Should().Be(ApplyChannel.Email);
        package.Email.Should().BeEquivalentTo(new
        {
            Recipient = "jobs@beispiel.de",
            Subject = "Bewerbung als Full-Stack-Entwickler (m/w/d)",
            Salutation = "Sehr geehrte Frau Müller,",
            Body = Packages.EmailBody,
            Attachments = Attachments,
        });
        package.Warnings.Should().BeEmpty();
        Generator.Calls.Should().Be(1);
        Packages.Calls.Should().Be(1);

        var ready = await Client.GetFromJsonAsync<List<ReadyPackageDto>>("/api/packages", ApiJson, CancellationToken);
        ready!.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Channel = ApplyChannel.Email, Email = "jobs@beispiel.de", Approved = false });

        (await Client.GetAsync($"/api/applications/{package.ApplicationId}/package.zip", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Conflict, "attachments exist only after approval");
        await PostJsonAsync($"/api/applications/{package.ApplicationId}/documents/approve", new { version = package.Version, acceptWarnings = true });
        var zip = await Client.GetAsync($"/api/applications/{package.ApplicationId}/package.zip", CancellationToken);
        zip.StatusCode.Should().Be(HttpStatusCode.OK);
        using var archive = new ZipArchive(await zip.Content.ReadAsStreamAsync(CancellationToken));
        archive.Entries.Select(e => e.Name).Should().BeEquivalentTo(package.Email.Attachments);
    }

    [Fact]
    public async Task A_portal_package_has_the_checklist_and_short_texts_and_the_fact_guard_checks_them()
    {
        Packages.EmailBody = "Ich habe jahrelang Kubernetes-Cluster betrieben.";
        var vacancy = await SeedVacancyAsync(PortalPosting);

        var package = await CreatePackageAsync(vacancy.Id);

        package.HowToApply.Should().BeEquivalentTo(new { Channel = ApplyChannel.Portal, PortalOnly = true });
        package.Portal.Checklist.Select(c => c.Label).Should().Contain(["Lebenslauf (PDF)", "Anschreiben (PDF)", "Gehaltsvorstellung", "Frühester Eintritt", "Hinweis zu Arbeitszeugnissen"]);
        package.Portal.Checklist.Single(c => c.Label == "Frühester Eintritt").Value.Should().NotBeNullOrEmpty();
        package.Portal.Motivation.Length.Should().BeLessThanOrEqualTo(PackageTexts.MaxMotivation);
        package.Portal.PostingUrl.Should().Be(vacancy.Url);
        package.Warnings.Should().ContainSingle(w => w.Code == DraftWarning.InventedTechnology && w.Message.Contains("Kubernetes"));
    }

    [Fact]
    public async Task The_estimate_lists_the_top_candidates_without_application_and_top_k_prepares_them()
    {
        await SeedVacancyAsync(EmailPosting);

        var estimate = await Client.GetFromJsonAsync<PackageEstimateDto>("/api/packages/estimate?k=5", ApiJson, CancellationToken);
        estimate!.Candidates.Should().ContainSingle();
        estimate.Calls.Should().Be(2);
        estimate.CostUsd.Should().BeGreaterThan(0);
        estimate.WithinLimit.Should().BeTrue();

        var results = await (await Client.PostAsync("/api/packages/top?k=5", null, CancellationToken)).Content.ReadFromJsonAsync<List<PackageResultDto>>(ApiJson, CancellationToken);
        results!.Should().ContainSingle().Which.ApplicationId.Should().NotBeNull();
        (await Client.GetFromJsonAsync<PackageEstimateDto>("/api/packages/estimate?k=5", ApiJson, CancellationToken))!.Candidates.Should().BeEmpty("it has an application now");
    }

    private async Task<PackageDto> CreatePackageAsync(Guid vacancyId)
    {
        var response = await Client.PostAsync($"/api/vacancies/{vacancyId}/package", null, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        return (await response.Content.ReadFromJsonAsync<PackageDto>(ApiJson, CancellationToken))!;
    }
}
