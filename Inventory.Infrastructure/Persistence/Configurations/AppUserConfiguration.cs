using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUsers");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(100)
            // Case-insensitive collation for username uniqueness
            .UseCollation("Latin1_General_CI_AS");

        builder.HasIndex(u => u.Username)
            .IsUnique();

        // EmployeeId unique FK is enforced by the one-to-one relationship in EmployeeConfiguration
        builder.HasIndex(u => u.EmployeeId)
            .IsUnique();

        builder.Property(u => u.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(u => u.MustChangePassword)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(u => u.FailedLoginCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(u => u.LockoutEnd)
            .IsRequired(false);

        builder.Property(u => u.LastLoginAt)
            .IsRequired(false);

        builder.HasMany(u => u.UserBranches)
            .WithOne(ub => ub.AppUser)
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
