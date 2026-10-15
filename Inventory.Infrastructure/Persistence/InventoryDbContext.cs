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
        _currentUser = currentUser ?? new DevCurrentUser();
        _clock = clock ?? new DateTimeClock();
    }

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();
    public DbSet<ApiFieldMapping> ApiFieldMappings => Set<ApiFieldMapping>();
    public DbSet<GrnApiResponse> GrnApiResponses => Set<GrnApiResponse>();

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
            .HasQueryFilter(m => m.ApiCredential != null && m.ApiCredential.BranchId == _currentUser.BranchId);
    }

    private void ConfigureBranchFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, IBranchScoped
    {
        // Enforce tenant/branch isolation via Global Query Filters.
        // NOTE: Admin cross-branch reads must use IgnoreQueryFilters() or Dapper explicitly.
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.BranchId == _currentUser.BranchId);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = _clock.UtcNow;
        var currentUserId = _currentUser.UserId;
        var currentBranchId = _currentUser.BranchId;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = utcNow;
                entry.Entity.CreatedBy = currentUserId;

                if (entry.Entity is IBranchScoped branchScoped)
                {
                    if (branchScoped.BranchId == 0)
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
                entry.Entity.ModifiedBy = currentUserId;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
