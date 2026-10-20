using System.Reflection;
using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Common;
using Inventory.Domain.Entities;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Persistence;

public class InventoryDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        ICurrentUser? currentUser = null,
        IClock? clock = null) : base(options)
    {
        _currentUser = currentUser ?? SystemCurrentUser.Instance;
        _clock = clock ?? new DateTimeClock();
    }

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();
    public DbSet<ApiFieldMapping> ApiFieldMappings => Set<ApiFieldMapping>();
    public DbSet<GrnApiResponse> GrnApiResponses => Set<GrnApiResponse>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagPrintLog> TagPrintLogs => Set<TagPrintLog>();
    public DbSet<TagCounter> TagCounters => Set<TagCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Global Query Filter: enforce branch isolation on all branch-scoped entities.
        // NOTE: Admin cross-branch reads must use IgnoreQueryFilters() or Dapper explicitly.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IBranchScoped).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(InventoryDbContext)
                    .GetMethod(nameof(ConfigureBranchFilter), BindingFlags.NonPublic | BindingFlags.Instance)?
                    .MakeGenericMethod(entityType.ClrType);

                method?.Invoke(this, new object[] { modelBuilder });
            }
        }

        // Explicitly define matching query filter for ApiFieldMapping (child of branch-scoped ApiCredential)
        modelBuilder.Entity<ApiFieldMapping>()
            .HasQueryFilter(m => _currentUser.IsSystem || (m.ApiCredential != null && m.ApiCredential.BranchId == _currentUser.BranchId));

        // Explicitly define matching query filter for TagPrintLog (child of branch-scoped Tag)
        modelBuilder.Entity<TagPrintLog>()
            .HasQueryFilter(l => _currentUser.IsSystem || (l.Tag != null && l.Tag.BranchId == _currentUser.BranchId));
    }

    private void ConfigureBranchFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, IBranchScoped
    {
        // Enforce tenant/branch isolation via Global Query Filters.
        // Bypassed when IsSystem is true (seeders/migrations).
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => _currentUser.IsSystem || e.BranchId == _currentUser.BranchId);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = _clock.UtcNow;
        var currentUserId = _currentUser.UserId;
        var currentBranchId = _currentUser.BranchId;
        var isSystem = _currentUser.IsSystem;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = utcNow;
                entry.Entity.CreatedBy = currentUserId == 0 ? null : currentUserId;

                if (entry.Entity is IBranchScoped branchScoped)
                {
                    if (isSystem)
                    {
                        // System scope: BranchId must be set explicitly by the caller.
                        // No auto-set and no cross-branch guard.
                    }
                    else if (branchScoped.BranchId == 0)
                    {
                        branchScoped.BranchId = currentBranchId;
                    }
                    else if (branchScoped.BranchId != currentBranchId)
                    {
                        throw new DomainException($"Cannot create branch-scoped entity for branch {branchScoped.BranchId} when current user belongs to branch {currentBranchId}.");
                    }
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAt = utcNow;
                entry.Entity.ModifiedBy = currentUserId == 0 ? null : currentUserId;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
