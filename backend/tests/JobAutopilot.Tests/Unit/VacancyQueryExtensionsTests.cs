using FluentAssertions;
using JobAutopilot.Infrastructure.Persistence;

namespace JobAutopilot.Tests.Unit;

public class VacancyQueryExtensionsTests
{
    [Theory]
    [InlineData("entwickler", "%entwickler%")]
    [InlineData("  C#  ", "%C#%")]
    [InlineData("50%", @"%50\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData(@"C:\dev", @"%C:\\dev%")]
    public void Contains_pattern_matches_user_input_literally(string input, string expected)
    {
        VacancyQueryExtensions.ContainsPattern(input).Should().Be(expected);
    }
}
