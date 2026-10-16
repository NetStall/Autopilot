using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobAutopilot.Infrastructure.Persistence;

/// <summary>
/// Npgsql only writes <see cref="DateTimeOffset"/> with offset 0 to <c>timestamptz</c>. Values with a local offset
/// (e.g. midnight Europe/Berlin) are normalised to UTC on the way in; they come back as UTC.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    value => value.ToUniversalTime(),
    value => value);
