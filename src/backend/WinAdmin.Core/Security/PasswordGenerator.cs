using System.Security.Cryptography;

namespace WinAdmin.Core.Security;

/// <summary>Случайный пароль (CSPRNG): есть заглавная, строчная, цифра и спецсимвол.</summary>
public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Special = "!@#$%^&*-_=+?";

    public static string Generate(int length = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 8);
        string all = Upper + Lower + Digits + Special;
        var chars = new char[length];
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[3] = Special[RandomNumberGenerator.GetInt32(Special.Length)];
        for (int i = 4; i < length; i++) chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
