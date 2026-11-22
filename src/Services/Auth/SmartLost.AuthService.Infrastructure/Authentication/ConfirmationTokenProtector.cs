using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SmartLost.AuthService.Infrastructure.Authentication;

public sealed class ConfirmationTokenProtector(IOptions<EmailConfirmationOptions> options)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key = Convert.FromBase64String(options.Value.EncryptionKey);

    public string Protect(string token, Guid id, string recipient, DateTimeOffset expiresAt)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(token);
        byte[] packed = new byte[NonceSize + TagSize + plaintext.Length];
        Span<byte> nonce = packed.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using AesGcm cipher = new(_key, TagSize);
        cipher.Encrypt(nonce, plaintext, packed.AsSpan(NonceSize + TagSize), packed.AsSpan(NonceSize, TagSize),
            AssociatedData(id, recipient, expiresAt));
        return Convert.ToBase64String(packed);
    }

    public string Unprotect(string encryptedToken, Guid id, string recipient, DateTimeOffset expiresAt)
    {
        byte[] packed = Convert.FromBase64String(encryptedToken);
        if (packed.Length <= NonceSize + TagSize)
        {
            throw new CryptographicException("Invalid confirmation token ciphertext.");
        }

        byte[] plaintext = new byte[packed.Length - NonceSize - TagSize];
        using AesGcm cipher = new(_key, TagSize);
        cipher.Decrypt(packed.AsSpan(0, NonceSize), packed.AsSpan(NonceSize + TagSize), packed.AsSpan(NonceSize, TagSize),
            plaintext, AssociatedData(id, recipient, expiresAt));
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] AssociatedData(Guid id, string recipient, DateTimeOffset expiresAt)
    {
        return Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"auth.email.confirmation_requested/v2|{id:D}|{expiresAt.UtcTicks}|{recipient}"));
    }
}
