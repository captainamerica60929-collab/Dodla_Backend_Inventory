using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(e => e.Code)
            .IsUnique();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Department)
            .HasMaxLength(100);

        builder.Property(e => e.Designation)
            .HasMaxLength(100);

        builder.Property(e => e.Phone)
            .HasMaxLength(20);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasOne(e => e.AppUser)
            .WithOne(u => u.Employee)
            .HasForeignKey<AppUser>(u => u.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
