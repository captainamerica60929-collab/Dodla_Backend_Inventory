using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class ApiFieldMappingConfiguration : IEntityTypeConfiguration<ApiFieldMapping>
{
    public void Configure(EntityTypeBuilder<ApiFieldMapping> builder)
    {
        builder.ToTable("ApiFieldMappings");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ApiCredentialId)
            .IsRequired();

        builder.Property(m => m.SourceField)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.TargetColumn)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(m => m.Format)
            .HasMaxLength(100);

        builder.Property(m => m.DefaultValue)
            .HasMaxLength(200);

        builder.Property(m => m.IsRequired)
            .IsRequired()
            .HasDefaultValue(false);

        // Unique index per (ApiCredentialId, TargetColumn)
        builder.HasIndex(m => new { m.ApiCredentialId, m.TargetColumn })
            .IsUnique();
    }
}
