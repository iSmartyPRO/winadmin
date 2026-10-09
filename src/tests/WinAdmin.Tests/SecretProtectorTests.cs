using System.Security.Cryptography;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

public sealed class SecretProtectorTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    private readonly AesGcmSecretProtector _protector = new(Key);

    [Fact]
    public void Round_trips_with_prefix_and_random_nonce()
    {
        string a = _protector.Protect("P@ss;word=1", "module:ad:ServicePassword");
        string b = _protector.Protect("P@ss;word=1", "module:ad:ServicePassword");

        Assert.StartsWith("enc:v1:", a);
        Assert.NotEqual(a, b); // новый nonce каждый раз
        Assert.True(ProtectedValue.IsProtected(a));
        Assert.Equal("P@ss;word=1", _protector.Unprotect(a, "module:ad:ServicePassword"));
    }

    [Fact]
    public void Value_cannot_be_moved_to_another_field()
    {
        string value = _protector.Protect("secret", "database:password");
        Assert.Throws<SecretUnavailableException>(() => _protector.Unprotect(value, "platform:jwt"));
    }

    [Fact]
    public void Other_key_or_tampering_is_rejected()
    {
        string value = _protector.Protect("secret", "p");
        var other = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));
        Assert.Throws<SecretUnavailableException>(() => other.Unprotect(value, "p"));

        var raw = Convert.FromBase64String(value["enc:v1:".Length..]);
        raw[13] ^= 0xFF;
        string tampered = "enc:v1:" + Convert.ToBase64String(raw);
        Assert.Throws<SecretUnavailableException>(() => _protector.Unprotect(tampered, "p"));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("enc:v1:not-base64!")]
    [InlineData("enc:v1:AAAA")]
    public void Malformed_values_are_rejected(string value)
        => Assert.Throws<SecretUnavailableException>(() => _protector.Unprotect(value, "p"));

    [Fact]
    public void Key_must_be_32_bytes()
        => Assert.Throws<ArgumentException>(() => new AesGcmSecretProtector(new byte[16]));

    [Fact]
    public void Unavailable_protector_explains_why()
    {
        var p = new UnavailableSecretProtector("ключ не найден");
        var ex = Assert.Throws<SecretUnavailableException>(() => p.Protect("x", "p"));
        Assert.Contains("ключ не найден", ex.Message);
    }
}
