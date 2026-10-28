using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder)
    {
        builder.ToTable("UserBranches");

        builder.HasKey(ub => new { ub.UserId, ub.BranchId });

        builder.HasOne(ub => ub.Branch)
            .WithMany()
            .HasForeignKey(ub => ub.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // AppUser -> UserBranches cascade is configured in AppUserConfiguration
    }
}
