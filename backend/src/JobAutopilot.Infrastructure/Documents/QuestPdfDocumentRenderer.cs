using JobAutopilot.Core.Documents;
using QuestPDF;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JobAutopilot.Infrastructure.Documents;

/// <summary>
/// Letter and CV in the look of my current documents: Inter, dark text (#1D2329), the teal accent (#2B5F5A) for role
/// line and section labels, grey (#4A535C) contact line, a rule under the header, labels in a left column (CV).
/// QuestPDF Community license (ADR 0010). Fonts are embedded in the assembly (Inter, SIL OFL), system fonts are not
/// used, so the output is the same on Windows and in Docker.
/// </summary>
public sealed class QuestPdfDocumentRenderer : IDocumentRenderer
{
    private const string Font = "Inter";
    private const string Text = "#1D2329";
    private const string Accent = "#2B5F5A";
    private const string Muted = "#4A535C";
    private const int PreviewDpi = 110;

    private static readonly Lazy<bool> Initialized = new(Initialize);

    public QuestPdfDocumentRenderer() => _ = Initialized.Value;

    public RenderedDocument RenderLetter(LetterPrint letter, RenderFormat format)
    {
        ArgumentNullException.ThrowIfNull(letter);
        return Render(Document.Create(c => c.Page(page => LetterPage(page, letter))).WithMetadata(Metadata(letter.SenderName, letter.Letter.Subject)), format);
    }

    public RenderedDocument RenderCv(CvDocument cv, RenderFormat format)
    {
        ArgumentNullException.ThrowIfNull(cv);
        var title = cv.Language == DocumentLanguage.De ? "Lebenslauf" : "CV";
        return Render(Document.Create(c => c.Page(page => CvPage(page, cv))).WithMetadata(Metadata(cv.Name, $"{title} {cv.Name}")), format);
    }

    private static RenderedDocument Render(IDocument document, RenderFormat format)
    {
        var pages = document.GenerateSvg().Count;
        var content = format == RenderFormat.Pdf
            ? document.GeneratePdf()
            : document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = PreviewDpi }).First();
        return new RenderedDocument(content, pages);
    }

    private static DocumentMetadata Metadata(string author, string title) => new()
    {
        Author = author,
        Title = title,
        Creator = "job-autopilot",
        Producer = "job-autopilot",
        CreationDate = DateTimeOffset.UnixEpoch,
        ModifiedDate = DateTimeOffset.UnixEpoch,
    };

    private static void LetterPage(PageDescriptor page, LetterPrint print)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(24, Unit.Millimetre);
        page.MarginVertical(18, Unit.Millimetre);
        page.DefaultTextStyle(t => t.FontFamily(Font).FontSize(10.5f).FontColor(Text).LineHeight(1.35f));

        page.Content().Column(column =>
        {
            Header(column, print.SenderName, print.RoleLine, print.SenderContacts, nameSize: 21);

            column.Item().PaddingTop(16).Column(recipient =>
            {
                foreach (var line in print.RecipientLines)
                {
                    recipient.Item().Text(line);
                }
            });

            column.Item().PaddingTop(14).AlignRight().Text(print.PlaceAndDate);
            column.Item().PaddingTop(14).Text(print.Letter.Subject).SemiBold();
            column.Item().PaddingTop(12).Text(print.Letter.Salutation);
            foreach (var paragraph in print.Letter.Paragraphs)
            {
                column.Item().PaddingTop(7).Text(paragraph);
            }

            column.Item().PaddingTop(12).Text(print.Letter.Closing);
            column.Item().PaddingTop(18).Text(print.SenderName);
        });
    }

    private static void CvPage(PageDescriptor page, CvDocument cv)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(18, Unit.Millimetre);
        page.MarginVertical(14, Unit.Millimetre);
        page.DefaultTextStyle(t => t.FontFamily(Font).FontSize(9).FontColor(Text).LineHeight(1.3f));

        page.Content().Column(column =>
        {
            Header(column, cv.Name, cv.RoleLine, cv.Contacts, nameSize: 25);

            if (!string.IsNullOrWhiteSpace(cv.Summary))
            {
                Section(column, cv.Labels.Profile, body => body.Item().Text(cv.Summary));
            }

            Section(column, cv.Labels.Experience, body =>
            {
                foreach (var (entry, index) in cv.Experience.Select((e, i) => (e, i)))
                {
                    body.Item().PaddingTop(index == 0 ? 0 : 8).Column(item =>
                    {
                        item.Item().Row(row =>
                        {
                            row.RelativeItem().Text(entry.Title).SemiBold().FontSize(10);
                            row.AutoItem().AlignRight().Text(entry.Period).FontColor(Muted);
                        });
                        item.Item().Text(entry.Where is null ? entry.Company : $"{entry.Company} · {entry.Where}").FontColor(Muted);
                        foreach (var bullet in entry.Visible)
                        {
                            item.Item().PaddingTop(2).PaddingLeft(4).Row(row =>
                            {
                                row.ConstantItem(8).Text("•").FontColor(Accent);
                                row.RelativeItem().Text(bullet.Text);
                            });
                        }
                    });
                }
            });

            var groups = cv.SkillGroups.Where(g => !g.Hidden).ToList();
            if (groups.Count > 0)
            {
                Section(column, cv.Labels.Skills, body =>
                {
                    foreach (var group in groups)
                    {
                        body.Item().PaddingBottom(1).Row(row =>
                        {
                            row.ConstantItem(32, Unit.Millimetre).Text(group.Name).SemiBold();
                            row.RelativeItem().Text(string.Join(", ", group.Skills));
                        });
                    }
                });
            }

            if (cv.Education.Count > 0)
            {
                Section(column, cv.Labels.Education, body =>
                {
                    foreach (var (education, index) in cv.Education.Select((e, i) => (e, i)))
                    {
                        body.Item().PaddingTop(index == 0 ? 0 : 6).Column(item =>
                        {
                            item.Item().Row(row =>
                            {
                                row.RelativeItem().Text(education.Degree).SemiBold().FontSize(10);
                                row.AutoItem().AlignRight().Text(education.Period).FontColor(Muted);
                            });
                            item.Item().Text(education.Institution).FontColor(Muted);
                            foreach (var note in education.Notes)
                            {
                                item.Item().PaddingTop(1).PaddingLeft(4).Row(row =>
                                {
                                    row.ConstantItem(8).Text("•").FontColor(Accent);
                                    row.RelativeItem().Text(note);
                                });
                            }
                        });
                    }
                });
            }

            if (cv.Languages.Count > 0)
            {
                Section(column, cv.Labels.Languages, body =>
                {
                    foreach (var language in cv.Languages)
                    {
                        body.Item().Row(row =>
                        {
                            row.ConstantItem(40, Unit.Millimetre).Text(language.Name).SemiBold();
                            row.RelativeItem().Text(language.Level);
                        });
                    }
                });
            }
        });
    }

    /// <summary>Name, role line, contact line with „ · " separators and links, rule below.</summary>
    private static void Header(ColumnDescriptor column, string name, string roleLine, IReadOnlyList<CvContact> contacts, float nameSize)
    {
        column.Item().Text(name).FontSize(nameSize).Bold();
        column.Item().PaddingTop(1).Text(roleLine).FontSize(11).FontColor(Accent);
        column.Item().PaddingTop(4).BorderBottom(1).BorderColor(Text).PaddingBottom(9).Text(text =>
        {
            text.DefaultTextStyle(t => t.FontSize(8.5f).FontColor(Muted));
            foreach (var (contact, index) in contacts.Select((c, i) => (c, i)))
            {
                if (index > 0)
                {
                    text.Span("  ·  ");
                }

                if (contact.Url is { } url)
                {
                    text.Hyperlink(contact.Text, url);
                }
                else
                {
                    text.Span(contact.Text);
                }
            }
        });
    }

    /// <summary>A CV section: the label in the accent colour in a left column, the content right of it.</summary>
    private static void Section(ColumnDescriptor column, string label, Action<ColumnDescriptor> content) =>
        column.Item().PaddingTop(12).Row(row =>
        {
            row.ConstantItem(33, Unit.Millimetre).PaddingTop(1).Text(label).FontSize(7.5f).SemiBold().FontColor(Accent).LetterSpacing(0.08f);
            row.RelativeItem().Column(content);
        });

    private static bool Initialize()
    {
        // Community license: individual use (ADR 0010).
        Settings.License = LicenseType.Community;
        Settings.UseSystemFonts = false;
        var assembly = typeof(QuestPdfDocumentRenderer).Assembly;
        foreach (var weight in new[] { "Regular", "Medium", "SemiBold", "Bold" })
        {
            using var stream = assembly.GetManifestResourceStream($"JobAutopilot.Infrastructure.Documents.Fonts.Inter-{weight}.ttf")
                ?? throw new InvalidOperationException($"Embedded font Inter-{weight}.ttf is missing.");
            FontManager.RegisterFontFromStream(stream);
        }

        return true;
    }
}
