using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("Materials");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Code)
            .IsRequired()
            .HasMaxLength(30);

        builder.HasIndex(m => m.Code)
            .IsUnique();

        builder.Property(m => m.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(m => m.Uom)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(m => m.Zone)
            .HasMaxLength(20);

        builder.Property(m => m.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
    }
}
