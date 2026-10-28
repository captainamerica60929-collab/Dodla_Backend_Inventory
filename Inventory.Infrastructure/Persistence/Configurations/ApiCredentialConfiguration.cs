using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class ApiCredentialConfiguration : IEntityTypeConfiguration<ApiCredential>
{
    public void Configure(EntityTypeBuilder<ApiCredential> builder)
    {
        builder.ToTable("ApiCredentials");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.BranchId)
            .IsRequired();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.BaseUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(c => c.HttpMethod)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("GET");

        builder.Property(c => c.AuthType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.UserName)
            .HasMaxLength(100);

        builder.Property(c => c.HeaderName)
            .HasMaxLength(100);

        builder.Property(c => c.SecretEncrypted)
            .HasMaxLength(2000);

        builder.Property(c => c.ExtraHeadersJson)
            .HasColumnType("nvarchar(max)");

        builder.Property(c => c.TimeoutSeconds)
            .IsRequired()
            .HasDefaultValue(30);

        builder.Property(c => c.ContentType)
            .HasMaxLength(50);

        builder.Property(c => c.RequestBodyTemplate)
            .HasColumnType("nvarchar(max)");

        builder.Property(c => c.ResponseRootPath)
            .HasMaxLength(100);

        builder.Property(c => c.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasMany(c => c.FieldMappings)
            .WithOne(m => m.ApiCredential)
            .HasForeignKey(m => m.ApiCredentialId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
