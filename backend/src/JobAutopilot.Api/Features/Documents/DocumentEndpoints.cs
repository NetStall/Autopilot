using JobAutopilot.Core.Documents;
using JobAutopilot.Infrastructure.Documents;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobAutopilot.Api.Features.Documents;

/// <summary>
/// Application documents (step 06): „Unterlagen erstellen", review, „Freigeben", PDFs, „Als gesendet markieren".
/// Nothing is sent by the tool; „Sent" records that I submitted the approved documents myself. Personal data —
/// localhost only like everything else (docs/SECURITY.md).
/// </summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/vacancies/{id:guid}/documents", CreateForVacancy).WithTags("Documents").WithName("CreateDocumentsForVacancy");

        var group = app.MapGroup("/api/applications/{id:guid}").WithTags("Documents");
        group.MapPost("/documents", CreateForApplication).WithName("CreateDocuments");
        group.MapGet("/documents", Get).WithName("GetDocuments");
        group.MapPut("/documents", Save).WithName("SaveDocuments");
        group.MapPost("/documents/regenerate", Regenerate).WithName("RegenerateLetter");
        group.MapPost("/documents/paragraphs/{index:int}/rephrase", Rephrase).WithName("RephraseParagraph");
        group.MapPost("/documents/restore", Restore).WithName("RestoreDocuments");
        group.MapPost("/documents/preview/{document}", Preview).WithName("PreviewDocument");
        group.MapPost("/documents/approve", Approve).WithName("ApproveDocuments");
        group.MapGet("/documents/{document}.pdf", Pdf).WithName("DownloadDocument");
        group.MapPost("/mark-sent", MarkSent).WithName("MarkApplicationSent");
        return app;
    }

    private static Task<IResult> CreateForVacancy(Guid id, ApplicationDocumentsService service, CancellationToken ct) =>
        Run(async () =>
        {
            var (view, created) = await service.CreateForVacancyAsync(id, ct);
            var dto = DocumentsDto.From(view);
            return created ? TypedResults.Created($"/api/applications/{dto.ApplicationId}/documents", dto) : TypedResults.Ok(dto);
        });

    private static Task<IResult> CreateForApplication(Guid id, ApplicationDocumentsService service, CancellationToken ct) =>
        Run(async () =>
        {
            var (view, created) = await service.CreateForApplicationAsync(id, ct);
            var dto = DocumentsDto.From(view);
            return created ? TypedResults.Created($"/api/applications/{id}/documents", dto) : TypedResults.Ok(dto);
        });

    private static Task<IResult> Get(Guid id, ApplicationDocumentsService service, CancellationToken ct) =>
        Run(async () => TypedResults.Ok(DocumentsDto.From(await service.GetAsync(id, ct))));

    private static Task<IResult> Save(Guid id, SaveDocumentsRequest request, ApplicationDocumentsService service, CancellationToken ct) =>
        Run(async () =>
        {
            var letter = request.Letter!.ToLetter(out var error);
            return letter is null
                ? Invalid(error!)
                : TypedResults.Ok(DocumentsDto.From(await service.SaveAsync(id, request.Version, letter, request.CvOptions!.ToOptions(), ct)));
        });

    private static Task<IResult> Regenerate(Guid id, VersionRequest request, ApplicationDocumentsService service, CancellationToken ct) =>
        Run(async () => TypedResults.Ok(DocumentsDto.From(await service.RegenerateAsync(id, request.Version, ct))));

    private static Task<IResult> Rephrase(Guid id, int index, RephraseRequest request, ApplicationDocumentsService service, CancellationToken ct) =>
        Run(async () => TypedResults.Ok(DocumentsDto.From(await service.RephraseAsync(id, request.Version, index, request.Hint, ct))));

    private static Task<IResult> Restore(Guid id, RestoreRequest request, ApplicationDocumentsService service, CancellationToken ct) =>
        Run(async () => TypedResults.Ok(DocumentsDto.From(await service.RestoreAsync(id, request.Version, request.Revision, ct))));

    /// <summary>PNG of page 1 of the posted (unsaved) state; header <c>X-Page-Count</c> says how many pages the PDF would have.</summary>
    private static Task<IResult> Preview(Guid id, string document, PreviewRequest request, ApplicationDocumentsService service, HttpContext http, CancellationToken ct) =>
        Run(async () =>
        {
            if (document is not ("letter" or "cv"))
            {
                return TypedResults.NotFound();
            }

            LetterDraft? letter = null;
            if (request.Letter is { } dto)
            {
                letter = dto.ToLetter(out var error);
                if (letter is null)
                {
                    return Invalid(error!);
                }
            }

            var rendered = await service.PreviewAsync(id, document == "letter", letter, request.CvOptions?.ToOptions(), ct);
            http.Response.Headers["X-Page-Count"] = rendered.Pages.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return TypedResults.File(rendered.Content, "image/png");
        });

    private static Task<IResult> Approve(Guid id, ApproveRequest request, ApplicationDocumentsService service, CancellationToken ct) =>
        Run(async () => TypedResults.Ok(DocumentsDto.From(await service.ApproveAsync(id, request.Version, request.AcceptWarnings, ct))));

    private static async Task<IResult> Pdf(Guid id, string document, ApplicationDocumentsService service, CancellationToken ct)
    {
        if (document is not ("letter" or "cv"))
        {
            return TypedResults.NotFound();
        }

        var file = await service.GetPdfAsync(id, document == "letter", ct);
        return file is { } found
            ? TypedResults.File(found.Content, "application/pdf", found.FileName)
            : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Noch nicht freigegeben.",
                detail: "PDFs entstehen beim Freigeben.");
    }

    private static Task<IResult> MarkSent(Guid id, MarkSentRequest request, ApplicationDocumentsService service, CancellationToken ct) =>
        Run(async () => TypedResults.Ok(DocumentsDto.From(await service.MarkSentAsync(id, request.Channel, request.At, ct))));

    internal static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DocumentException ex)
        {
            var (status, title) = ex.Problem switch
            {
                DocumentProblem.NotFound => (StatusCodes.Status404NotFound, "Nicht gefunden."),
                DocumentProblem.Conflict => (StatusCodes.Status409Conflict, "Konflikt."),
                DocumentProblem.Invalid => (StatusCodes.Status400BadRequest, "Ungültige Eingabe."),
                DocumentProblem.LimitReached => (StatusCodes.Status429TooManyRequests, "Limit erreicht."),
                DocumentProblem.ProfileMissing => (StatusCodes.Status422UnprocessableEntity, "Profil fehlt."),
                _ => ex.GenerationError switch
                {
                    LetterGenerationError.NotConfigured => (StatusCodes.Status503ServiceUnavailable, "Claude ist nicht konfiguriert."),
                    LetterGenerationError.RateLimited => (StatusCodes.Status429TooManyRequests, "Claude ist ausgelastet."),
                    LetterGenerationError.Timeout => (StatusCodes.Status504GatewayTimeout, "Zeitüberschreitung."),
                    LetterGenerationError.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Claude nicht erreichbar."),
                    _ => (StatusCodes.Status502BadGateway, "Anschreiben konnte nicht erstellt werden."),
                },
            };
            var extensions = new Dictionary<string, object?>();
            if (ex.GenerationError is { } error)
            {
                extensions["generationError"] = error.ToString();
            }

            return TypedResults.Problem(statusCode: status, title: title, detail: ex.Message, extensions: extensions);
        }
    }

    private static ProblemHttpResult Invalid(string message) =>
        TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültige Eingabe.", detail: message);
}
