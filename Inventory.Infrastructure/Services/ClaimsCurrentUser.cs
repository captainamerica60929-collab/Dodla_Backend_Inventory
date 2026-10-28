using System.Security.Claims;
using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// Reads the current user context from JWT claims injected by the authentication middleware.
/// Claims: sub (UserId), branch_id (BranchId), role (Role), name, employee_id.
/// </summary>
public class ClaimsCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsSystem => false;

    public int UserId
    {
        get
        {
            var user = User;
            if (user?.Identity?.IsAuthenticated != true)
                return 0;

            var sub = user.FindFirstValue("sub");

            if (int.TryParse(sub, out var id) && id > 0)
                return id;

            throw new UnauthorizedException("Invalid credentials");
        }
    }

    public int BranchId
    {
        get
        {
            var user = User;
            if (user?.Identity?.IsAuthenticated != true)
                return -1; // Anonymous requests get BranchId = -1 (matches no rows)

            var bid = user.FindFirstValue("branch_id");

            if (int.TryParse(bid, out var id) && id > 0)
                return id;

            throw new UnauthorizedException("Invalid credentials");
        }
    }

    public string Role => User?.FindFirstValue("role") ?? string.Empty;

    public string Name => User?.FindFirstValue("name") ?? string.Empty;

    public int? EmployeeId
    {
        get
        {
            var empId = User?.FindFirstValue("employee_id");
            return int.TryParse(empId, out var id) && id > 0 ? id : null;
        }
    }
}
