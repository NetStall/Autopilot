using FluentAssertions;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Fakes;

namespace JobAutopilot.Tests.Unit.Documents;

public class ApplicationWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static Vacancy Vacancy() => new()
    {
        Id = Guid.NewGuid(),
        Source = VacancySource.Arbeitsagentur,
        ExternalId = "1",
        Title = "Full-Stack-Entwickler (m/w/d)",
        Company = "Beispiel GmbH",
        Url = "https://example.org/job",
    };

    private static ApplicationDraft Draft(Application application, params DraftWarning[] warnings)
    {
        var letter = FakeLetterGenerator.CleanLetter(DocumentTestData.Input());
        var draft = new ApplicationDraft
        {
            Id = Guid.NewGuid(), ApplicationId = application.Id, Letter = letter, PromptVersion = "letter.v1", Model = "m",
            LetterFile = "a.pdf", CvFile = "b.pdf",
        };
        draft.Record(DraftRevisionKind.Generated, letter, CvOptions.Default, Now, new TokenUsage(100, 50), "m", "letter.v1");
        draft.Warnings = [.. warnings];
        return draft;
    }

    [Fact]
    public void Draft_then_approved_then_sent()
    {
        var application = ApplicationWorkflow.NewDraft(Vacancy(), Now);
        application.Status.Should().Be(ApplicationStatus.Draft);
        var draft = Draft(application);

        ApplicationWorkflow.Approve(application, draft, Now.AddMinutes(5), acceptWarnings: false);
        application.Status.Should().Be(ApplicationStatus.Approved);
        application.Documents.Should().Equal("a.pdf", "b.pdf");
        draft.ApprovedAt.Should().Be(Now.AddMinutes(5));

        ApplicationWorkflow.MarkSent(application, draft, "E-Mail", Now.AddHours(1), Now.AddHours(1));
        application.Status.Should().Be(ApplicationStatus.Sent);
        application.Channel.Should().Be("E-Mail");
        application.AppliedAt.Should().Be(Now.AddHours(1));
        application.FollowUpAt.Should().Be(Now.AddHours(1).AddDays(7));
        application.Events.Select(e => e.To).Should().Equal(ApplicationStatus.Draft, ApplicationStatus.Approved, ApplicationStatus.Sent);
    }

    [Fact]
    public void Approval_with_open_warnings_needs_a_confirmation()
    {
        var application = ApplicationWorkflow.NewDraft(Vacancy(), Now);
        var draft = Draft(application, new DraftWarning(DraftWarning.InventedNumber, "12"), new DraftWarning(DraftWarning.Cliche, "x", 0, WarningSeverity.Hint));

        var refused = () => ApplicationWorkflow.Approve(application, draft, Now, acceptWarnings: false);
        refused.Should().Throw<WorkflowException>().WithMessage("*offene Warnungen*");
        application.Status.Should().Be(ApplicationStatus.Draft);

        ApplicationWorkflow.Approve(application, draft, Now, acceptWarnings: true);
        application.Status.Should().Be(ApplicationStatus.Approved);
        draft.WarningsAccepted.Should().BeTrue();
    }

    [Fact]
    public void Hints_alone_do_not_need_a_confirmation()
    {
        var application = ApplicationWorkflow.NewDraft(Vacancy(), Now);
        var draft = Draft(application, new DraftWarning(DraftWarning.Cliche, "x", 0, WarningSeverity.Hint));

        ApplicationWorkflow.Approve(application, draft, Now, acceptWarnings: false);

        application.Status.Should().Be(ApplicationStatus.Approved);
        draft.WarningsAccepted.Should().BeFalse();
    }

    [Fact]
    public void Nothing_reaches_sent_without_the_approval()
    {
        var application = ApplicationWorkflow.NewDraft(Vacancy(), Now);
        var draft = Draft(application);

        var send = () => ApplicationWorkflow.MarkSent(application, draft, "E-Mail", Now, Now);
        send.Should().Throw<WorkflowException>();
        var sendWithoutDraft = () => ApplicationWorkflow.MarkSent(application, null, "E-Mail", Now, Now);
        sendWithoutDraft.Should().Throw<WorkflowException>();
        application.Status.Should().Be(ApplicationStatus.Draft);
    }

    [Fact]
    public void A_change_after_approval_needs_a_new_approval()
    {
        var application = ApplicationWorkflow.NewDraft(Vacancy(), Now);
        var draft = Draft(application);
        ApplicationWorkflow.Approve(application, draft, Now, acceptWarnings: false);

        draft.Record(DraftRevisionKind.Saved, draft.Letter with { Subject = "Neu" }, draft.CvOptions, Now.AddMinutes(1));
        ApplicationWorkflow.Reopen(application, Now.AddMinutes(1));

        draft.ApprovedAt.Should().BeNull();
        application.Status.Should().Be(ApplicationStatus.Draft);
        var send = () => ApplicationWorkflow.MarkSent(application, draft, "E-Mail", Now, Now);
        send.Should().Throw<WorkflowException>();
    }

    [Theory]
    [InlineData(ApplicationStatus.Sent, ApplicationStatus.Interview, true)]
    [InlineData(ApplicationStatus.Interview, ApplicationStatus.Offer, true)]
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Withdrawn, true)]
    [InlineData(ApplicationStatus.Approved, ApplicationStatus.Withdrawn, true)]
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Sent, false)]
    [InlineData(ApplicationStatus.Approved, ApplicationStatus.Sent, false)]
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Approved, false)]
    [InlineData(ApplicationStatus.Draft, ApplicationStatus.Interview, false)]
    [InlineData(ApplicationStatus.Rejected, ApplicationStatus.Sent, false)]
    [InlineData(ApplicationStatus.Sent, ApplicationStatus.Draft, false)]
    public void Manual_status_changes_cannot_skip_approval(ApplicationStatus from, ApplicationStatus to, bool allowed) =>
        ApplicationWorkflow.AllowsManualChange(from, to).Should().Be(allowed);

    [Fact]
    public void Every_change_is_a_numbered_revision_with_its_tokens()
    {
        var application = ApplicationWorkflow.NewDraft(Vacancy(), Now);
        var draft = Draft(application);
        draft.Record(DraftRevisionKind.Rephrased, draft.Letter, draft.CvOptions, Now, new TokenUsage(10, 5), "m", "letter.v1", "Absatz 2");
        draft.Record(DraftRevisionKind.Saved, draft.Letter, draft.CvOptions, Now);

        draft.Revisions.Select(r => (r.Number, r.Kind)).Should().Equal(
            (1, DraftRevisionKind.Generated), (2, DraftRevisionKind.Rephrased), (3, DraftRevisionKind.Saved));
        draft.Version.Should().Be(3);
        draft.Usage.Should().Be(new TokenUsage(110, 55));
        draft.Calls.Should().Be(2);
        draft.Revisions[2].Model.Should().BeNull();
    }
}
