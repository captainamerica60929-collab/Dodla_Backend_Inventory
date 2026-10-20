using Inventory.Domain.Common;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;

namespace Inventory.Domain.Entities;

/// <summary>
/// Application user — global (not branch-scoped).
/// A user can log in to any of their allowed branches (UserBranches).
/// </summary>
public class AppUser : BaseEntity
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public string Username { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public UserRole Role { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Navigations
    public Employee Employee { get; set; } = null!;
    public ICollection<UserBranch> UserBranches { get; set; } = new List<UserBranch>();

    /// <summary>
    /// Record a failed login attempt. On the 5th failure, lock the account for 15 minutes.
    /// </summary>
    public void RecordFailedLogin(DateTime utcNow)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedAttempts)
            LockoutEnd = utcNow.Add(LockoutDuration);
    }

    /// <summary>
    /// Reset the failed login counter and record the successful login time.
    /// </summary>
    public void ResetFailedLogins(DateTime utcNow)
    {
        FailedLoginCount = 0;
        LockoutEnd = null;
        LastLoginAt = utcNow;
    }

    /// <summary>Deactivate this user account.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Activate this user account.
    /// Throws if the linked employee is inactive.
    /// </summary>
    public void Activate()
    {
        if (!Employee.IsActive)
            throw new DomainException("Cannot activate a user whose employee record is inactive.");
        IsActive = true;
    }

    /// <summary>Whether the account is currently locked out.</summary>
    public bool IsLockedOut(DateTime utcNow) =>
        LockoutEnd.HasValue && LockoutEnd.Value > utcNow;

    /// <summary>Unlock the account manually (admin action).</summary>
    public void Unlock()
    {
        FailedLoginCount = 0;
        LockoutEnd = null;
    }
}
