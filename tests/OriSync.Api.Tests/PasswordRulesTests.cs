using OriSync.Api.Authentication;

namespace OriSync.Api.Tests;

public sealed class PasswordRulesTests
{
    [Theory]
    [InlineData("Secure!1")]
    [InlineData("Longer Password!")]
    public void PermanentPasswordsAcceptTheConfirmedPolicy(string password)
    {
        Assert.True(PasswordRules.IsValid(password));
    }

    [Theory]
    [InlineData("Short!A")]
    [InlineData("lowercase!")]
    [InlineData("UPPERCASE!")]
    [InlineData("NoSpecialPassword")]
    [InlineData("")]
    public void PermanentPasswordsRejectAnyMissingRequirement(string password)
    {
        Assert.False(PasswordRules.IsValid(password));
    }

    [Fact]
    public void TemporaryBootstrapPasswordOnlyRequiresEightCharacters()
    {
        Assert.True(PasswordRules.IsValidTemporary("temporary"));
        Assert.False(PasswordRules.IsValidTemporary("1234567"));
    }
}
