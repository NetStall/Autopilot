namespace JobAutopilot.Api.Common;

/// <summary>One page of a list plus what the client needs to render paging.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
