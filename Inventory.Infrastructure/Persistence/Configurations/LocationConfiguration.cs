using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.BranchId)
            .IsRequired();

        builder.Property(l => l.Code)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(l => l.Zone)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(l => l.Capacity)
            .IsRequired();

        builder.Property(l => l.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(l => l.RowVersion)
            .IsRowVersion();

        builder.HasIndex(l => new { l.BranchId, l.Code })
            .IsUnique();

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(l => l.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
