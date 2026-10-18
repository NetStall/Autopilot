using FluentAssertions;
using JobAutopilot.Api.Common;

namespace JobAutopilot.Tests.Unit;

public class ApiSecurityTests
{
    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("https://jobs.example.de")]
    public void Absolute_http_origins_are_accepted(string origin)
    {
        var act = () => ApiSecurity.ValidateOrigins([origin]);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("http://*.example.de")]
    [InlineData("http://localhost:3000/app")]
    [InlineData("localhost:3000")]
    [InlineData("ftp://localhost")]
    public void Wildcards_paths_and_other_schemes_are_rejected(string origin)
    {
        var act = () => ApiSecurity.ValidateOrigins([origin]);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{origin}*");
    }
}
