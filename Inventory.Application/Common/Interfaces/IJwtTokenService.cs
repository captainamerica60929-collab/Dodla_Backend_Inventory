using Inventory.Domain.Entities;

namespace Inventory.Application.Common.Interfaces;

public interface IJwtTokenService
{
    /// <summary>
    /// Generates a signed JWT for the given user scoped to the given branch.
    /// Returns the token string and its expiry time (UTC).
    /// </summary>
    (string Token, DateTime ExpiresAt) GenerateToken(AppUser user, int branchId);
}
