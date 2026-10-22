using System.Text.Json.Serialization;
using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Applications;
using JobAutopilot.Api.Features.Documents;
using JobAutopilot.Api.Features.DailyRun;
using JobAutopilot.Api.Features.Evaluation;
using JobAutopilot.Api.Features.Extraction;
using JobAutopilot.Api.Features.Health;
using JobAutopilot.Api.Features.Llm;
using JobAutopilot.Api.Features.Profile;
using JobAutopilot.Api.Features.Scoring;
using JobAutopilot.Api.Features.SearchProfiles;
using JobAutopilot.Api.Features.Statistics;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Infrastructure.SearchProfiles;
using JobAutopilot.Api.Features.Settings;
using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Infrastructure;
using JobAutopilot.Core.Profile;
using JobAutopilot.Infrastructure.Applications;
using JobAutopilot.Infrastructure.Documents;
using JobAutopilot.Infrastructure.Persistence;
using JobAutopilot.Infrastructure.Profile;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOptions<ImportOptions>()
    .Bind(builder.Configuration.GetSection(ImportOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ImportOptions>, ImportOptionsValidator>();
builder.Services.Configure<UiOptions>(builder.Configuration.GetSection(UiOptions.SectionName));

// The candidate profile (personal data, git-ignored file); re-read when it changes.
var profilePath = Path.GetFullPath(
    builder.Configuration.GetSection(ProfileOptions.SectionName).Get<ProfileOptions>()?.Path ?? new ProfileOptions().Path,
    builder.Environment.ContentRootPath);
var trackerImportPath = Path.GetFullPath(
    builder.Configuration.GetSection(ApplicationOptions.SectionName).Get<ApplicationOptions>()?.TrackerImportPath
        ?? new ApplicationOptions().TrackerImportPath,
    builder.Environment.ContentRootPath);
builder.Services.AddSingleton<ICandidateProfileSource>(sp => new FileCandidateProfileSource(
    profilePath, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<FileCandidateProfileSource>>()));

// Approved PDFs (personal data, git-ignored folder under data/).
var documentsPath = Path.GetFullPath(
    builder.Configuration.GetSection(DocumentOptions.SectionName).Get<DocumentOptions>()?.OutputPath ?? new DocumentOptions().OutputPath,
    builder.Environment.ContentRootPath);
builder.Services.AddSingleton(new DocumentFileStore(documentsPath));

// Suchprofil (database): what I am looking for. Seeded once from appsettings.json + profile.json preferences (ADR 0009).
builder.Services.Configure<SearchProfileDefaults>(builder.Configuration.GetSection(SearchProfileDefaults.SectionName));
builder.Services.AddSingleton(sp => new SearchProfileStore(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<SearchProfileStore>>(),
    () => SearchProfileSeed.Create(
        sp.GetRequiredService<IOptions<ImportOptions>>().Value.Searches,
        sp.GetRequiredService<IOptions<ImportOptions>>().Value.ExcludeTitleTerms,
        sp.GetRequiredService<IOptions<RelevanceOptions>>().Value,
        sp.GetRequiredService<IOptions<FitOptions>>().Value.Penalties,
        sp.GetRequiredService<IOptions<SearchProfileDefaults>>().Value,
        sp.GetRequiredService<ICandidateProfileSource>().Current.Profile?.Preferences)));
builder.Services.AddSingleton<ISearchProfileSource>(sp => sp.GetRequiredService<SearchProfileStore>());
builder.Services.AddHostedService<SearchProfileInitializer>();

// Background import: endpoints and scheduler queue runs, one worker executes them (ADR 0006, 0007).
builder.Services.AddSingleton<ImportJobChannel>();
builder.Services.AddSingleton<ImportCoordinator>();
builder.Services.AddHostedService<ImportWorker>();
builder.Services.AddHostedService<ImportScheduler>();

// Re-scoring all vacancies in the background (UI „Neu bewerten", start-up).
builder.Services.AddSingleton<RescoreCoordinator>();
builder.Services.AddHostedService<RescoreWorker>();

// Requirement extraction with the local model (step 07a): one GPU job at a time, in the background.
builder.Services.AddSingleton<JobAutopilot.Api.Features.Extraction.ExtractionCoordinator>();
builder.Services.AddHostedService<JobAutopilot.Api.Features.Extraction.ExtractionWorker>();

// „Tageslauf" (08b): import → filters → scoring → KI → channels → summary, one at a time in the background.
builder.Services.AddSingleton<JobAutopilot.Api.Features.DailyRun.DailyRunCoordinator>();
builder.Services.AddSingleton<JobAutopilot.Api.Features.DailyRun.ILocalModelProbe, JobAutopilot.Api.Features.DailyRun.OllamaModelProbe>();
builder.Services.AddHostedService<JobAutopilot.Api.Features.DailyRun.DailyRunWorker>();

builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.AddApiSecurity();

// A running import gets this long to observe cancellation; it saves only at its very end, so nothing half-written.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

var app = builder.Build();

// ProblemDetails for every error; outside Development without exception details or stack traces.
// Malformed request bodies (bad JSON, unknown enum value) are client errors, not 500s.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseApiSecurity();
app.UseRouting();
app.UseCors(ApiSecurity.FrontendCorsPolicy);
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Explicit flag instead of "IsDevelopment": compose runs Production and still migrates; tests migrate themselves.
if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.MapHealthEndpoints();
app.MapVacancyEndpoints();
app.MapImportEndpoints();
app.MapUiSettingsEndpoints();
app.MapProfileEndpoints();
app.MapApplicationEndpoints(trackerImportPath);
app.MapSearchProfileEndpoints();
app.MapDocumentEndpoints();
app.MapStatisticsEndpoints();
app.MapChannelStatisticsEndpoints();
app.MapLlmEndpoints();
app.MapExtractionEndpoints();
app.MapEvaluationEndpoints();
app.MapDailyRunEndpoints();

await app.RunAsync();
