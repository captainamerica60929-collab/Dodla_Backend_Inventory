using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Inventory.Infrastructure.Auth;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly string _key;

    public JwtTokenService(JwtSettings settings, IConfiguration configuration)
    {
        _settings = settings;

        if (string.IsNullOrWhiteSpace(_settings.Issuer))
            throw new InvalidOperationException("JWT issuer (Jwt:Issuer) is not configured.");

        if (string.IsNullOrWhiteSpace(_settings.Audience))
            throw new InvalidOperationException("JWT audience (Jwt:Audience) is not configured.");

        if (_settings.LifetimeMinutes < 5 || _settings.LifetimeMinutes > 1440)
            throw new InvalidOperationException("JWT lifetime (Jwt:LifetimeMinutes) must be between 5 and 1440 minutes.");

        _key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "JWT signing key (Jwt:Key) is not configured. " +
                "Add it via user-secrets: dotnet user-secrets set \"Jwt:Key\" \"<your-32+-char-key>\"");

        if (Encoding.UTF8.GetByteCount(_key) < 32)
            throw new InvalidOperationException(
                "JWT signing key (Jwt:Key) must be at least 32 bytes. " +
                "Please provide a longer key via user-secrets.");
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(AppUser user, int branchId)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.LifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("name", user.Employee.Name),
            new Claim("role", user.Role.ToString()),
            new Claim("branch_id", branchId.ToString()),
            new Claim("employee_id", user.EmployeeId.ToString()),
        };

        var keyBytes = Encoding.UTF8.GetBytes(_key);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
