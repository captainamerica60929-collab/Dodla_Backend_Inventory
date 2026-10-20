using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// Password hashing using ASP.NET Core Identity's PasswordHasher (PBKDF2).
/// </summary>
public class PasswordService : IPasswordService
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string Hash(string plaintext)
        => _hasher.HashPassword(null!, plaintext);

    public bool Verify(string plaintext, string hash)
    {
        var result = _hasher.VerifyHashedPassword(null!, hash, plaintext);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
