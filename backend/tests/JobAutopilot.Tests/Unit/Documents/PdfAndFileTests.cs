using System.Text;
using FluentAssertions;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Profile;
using JobAutopilot.Infrastructure.Documents;
using JobAutopilot.Tests.Fakes;

namespace JobAutopilot.Tests.Unit.Documents;

/// <summary>PDF smoke tests (one page, name, links) and the file store.</summary>
public sealed class PdfAndFileTests : IDisposable
{
    private readonly QuestPdfDocumentRenderer _renderer = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("documents-tests-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CvDocument Cv(CandidateProfile? profile = null) =>
        CvBuilder.Build(profile ?? DocumentTestData.Profile(), DocumentTestData.Title, DocumentTestData.Description, DocumentLanguage.De);

    private static LetterPrint Letter()
    {
        var input = DocumentTestData.Input();
        return LetterPrint.Create(DocumentTestData.Profile(), FakeLetterGenerator.CleanLetter(input), input.Vacancy, input.Tone, Cv().RoleLine, DocumentTestData.Today);
    }

    [Fact]
    public void Cv_pdf_has_one_page_with_name_and_links()
    {
        var pdf = _renderer.RenderCv(Cv(), RenderFormat.Pdf);
        var text = Encoding.Latin1.GetString(pdf.Content);

        pdf.Pages.Should().Be(1);
        text.Should().StartWith("%PDF-").And.Contain("/Count 1");
        text.Should().Contain("/Author (Alex Beispiel)");
        text.Should().Contain("mailto:alex@example.org").And.Contain("https://github.com/example").And.Contain("https://example.org");
    }

    [Fact]
    public void Letter_pdf_has_one_page_with_name_and_links()
    {
        var pdf = _renderer.RenderLetter(Letter(), RenderFormat.Pdf);
        var text = Encoding.Latin1.GetString(pdf.Content);

        pdf.Pages.Should().Be(1);
        text.Should().Contain("/Author (Alex Beispiel)").And.Contain("https://example.org").And.Contain("mailto:alex@example.org");
    }

    [Fact]
    public void Preview_is_a_png_of_the_first_page()
    {
        var png = _renderer.RenderLetter(Letter(), RenderFormat.Png);

        png.Content.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        png.Pages.Should().Be(1);
    }

    [Fact]
    public void Pages_are_counted_for_a_cv_that_is_too_long()
    {
        var example = DocumentTestData.Profile();
        var bullets = Enumerable.Range(0, 60).Select(i => new Bullet { Text = $"Achievement number {i} with a fairly long description of what was done and why it mattered." }).ToList();
        var profile = new CandidateProfile { Personal = example.Personal, Summary = example.Summary, Skills = example.Skills, Experience = [new ExperienceEntry { Company = "A", Title = "Dev", From = "2020-01", Bullets = bullets }] };

        var cv = CvBuilder.Build(profile, "Dev", null, DocumentLanguage.En, CvOptions.Default with { ShownBullets = [.. Enumerable.Range(0, 60).Select(i => $"exp0.b{i}")] });

        _renderer.RenderCv(cv, RenderFormat.Png).Pages.Should().BeGreaterThan(1);
    }

    [Theory]
    [InlineData("Max Mustermann", DocumentLanguage.De, true, "Müller & Söhne GmbH", "Max_Mustermann_Anschreiben_Mueller_Soehne_GmbH.pdf")]
    [InlineData("Max Mustermann", DocumentLanguage.De, false, "ACME", "Max_Mustermann_Lebenslauf_ACME.pdf")]
    [InlineData("Max Mustermann", DocumentLanguage.En, true, "Acme Inc.", "Max_Mustermann_Cover_Letter_Acme_Inc.pdf")]
    [InlineData("Max Mustermann", DocumentLanguage.En, false, "../../etc/passwd", "Max_Mustermann_CV_etc_passwd.pdf")]
    [InlineData("Max Mustermann", DocumentLanguage.De, false, "???", "Max_Mustermann_Lebenslauf_Firma.pdf")]
    public void File_names_are_safe(string person, DocumentLanguage language, bool letter, string company, string expected) =>
        DocumentFileStore.FileName(person, language, letter, company).Should().Be(expected);

    [Fact]
    public async Task Files_are_written_read_replaced_and_deleted()
    {
        var store = new DocumentFileStore(_dir);
        var id = Guid.NewGuid();

        await store.WriteAsync(id, new Dictionary<string, byte[]> { ["a.pdf"] = [1], ["b.pdf"] = [2] }, Token);
        (await store.ReadAsync(id, "a.pdf", Token)).Should().Equal(1);

        await store.WriteAsync(id, new Dictionary<string, byte[]> { ["c.pdf"] = [3] }, Token);
        (await store.ReadAsync(id, "a.pdf", Token)).Should().BeNull("older PDFs of the application are replaced");
        Directory.GetFiles(Path.Combine(_dir, id.ToString())).Should().ContainSingle();

        store.Delete(id);
        Directory.Exists(Path.Combine(_dir, id.ToString())).Should().BeFalse();
    }

    [Theory]
    [InlineData("../x.pdf")]
    [InlineData("x.txt")]
    [InlineData("sub/x.pdf")]
    public async Task Paths_outside_the_folder_are_rejected(string name)
    {
        var act = () => new DocumentFileStore(_dir).ReadAsync(Guid.NewGuid(), name, Token);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
