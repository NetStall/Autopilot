namespace JobAutopilot.Api.Features.VacancyImports;

public sealed record ImportResultDto(int Fetched, int Added, int Updated, int Unchanged, long DurationMs);
