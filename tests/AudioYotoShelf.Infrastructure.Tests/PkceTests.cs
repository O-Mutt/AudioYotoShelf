using AudioYotoShelf.Infrastructure.Services;
using FluentAssertions;

namespace AudioYotoShelf.Infrastructure.Tests;

public class PkceTests
{
    [Fact]
    public void S256Challenge_MatchesTheRfc7636AppendixBExample()
    {
        Pkce.S256Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")
            .Should().Be("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }

    [Fact]
    public void CreateVerifier_IsAnUnpaddedUrlSafeStringOfTheMinimumRfcLength()
    {
        Pkce.CreateVerifier().Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
    }
}
