using System.Text.Json.Serialization;
using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Health;
using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Infrastructure;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOptions<ImportOptions>()
    .Bind(builder.Configuration.GetSection(ImportOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ImportOptions>, ImportOptionsValidator>();

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

await app.RunAsync();
