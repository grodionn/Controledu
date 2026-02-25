using Controledu.Common.Security;

namespace Controledu.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void CreateHash_AndVerify_WithCorrectPassword_ReturnsTrue()
    {
        var record = PasswordHasher.CreateHash("StrongPassword!123");

        var verified = PasswordHasher.Verify("StrongPassword!123", record);

        Assert.True(verified);
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFalse()
    {
        var record = PasswordHasher.CreateHash("StrongPassword!123");

        var verified = PasswordHasher.Verify("WrongPassword", record);

        Assert.False(verified);
    }
}
