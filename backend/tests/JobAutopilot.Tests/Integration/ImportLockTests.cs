using FluentAssertions;
using JobAutopilot.Core.Import;
using Microsoft.Extensions.DependencyInjection;

namespace JobAutopilot.Tests.Integration;

/// <summary>The PostgreSQL advisory lock against a real server.</summary>
public class ImportLockTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private IImportLock Lock => Factory.Services.GetRequiredService<IImportLock>();

    [Fact]
    public async Task Only_one_holder_at_a_time()
    {
        await using var first = await Lock.TryAcquireAsync(CancellationToken);
        var second = await Lock.TryAcquireAsync(CancellationToken);

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task Lock_can_be_taken_again_after_release()
    {
        var first = await Lock.TryAcquireAsync(CancellationToken);
        await first!.DisposeAsync();

        await using var again = await Lock.TryAcquireAsync(CancellationToken);

        again.Should().NotBeNull();
    }

    [Fact]
    public async Task Concurrent_attempts_have_exactly_one_winner()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Lock.TryAcquireAsync(CancellationToken)));

        try
        {
            attempts.Count(a => a is not null).Should().Be(1);
        }
        finally
        {
            foreach (var handle in attempts.Where(a => a is not null))
            {
                await handle!.DisposeAsync();
            }
        }
    }
}
