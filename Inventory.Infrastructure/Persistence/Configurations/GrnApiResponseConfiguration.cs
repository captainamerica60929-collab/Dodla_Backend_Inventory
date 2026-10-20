using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class GrnApiResponseConfiguration : IEntityTypeConfiguration<GrnApiResponse>
{
    public void Configure(EntityTypeBuilder<GrnApiResponse> builder)
    {
        builder.ToTable("GrnApiResponses");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.BranchId)
            .IsRequired();

        builder.Property(r => r.ApiCredentialId)
            .IsRequired();

        builder.Property(r => r.FetchBatchId)
            .IsRequired();

        builder.Property(r => r.FetchedAt)
            .IsRequired();

        builder.Property(r => r.GrnNo)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(r => r.GrnDate)
            .HasColumnType("datetime2");

        builder.Property(r => r.InvoiceNo)
            .HasMaxLength(50);

        builder.Property(r => r.InvoiceDate)
            .HasColumnType("date");

        builder.Property(r => r.Qty)
            .HasPrecision(18, 3);

        builder.Property(r => r.Uom)
            .HasMaxLength(10);

        builder.Property(r => r.MaterialCode)
            .HasMaxLength(50);

        builder.Property(r => r.PartName)
            .HasMaxLength(200);

        builder.Property(r => r.Description)
            .HasMaxLength(200);

        builder.Property(r => r.Plant)
            .HasMaxLength(10);

        builder.Property(r => r.StorageLocation)
            .HasMaxLength(20);

        builder.Property(r => r.BatchNo)
            .HasMaxLength(50);

        builder.Property(r => r.PoNo)
            .HasMaxLength(30);

        builder.Property(r => r.PoItem)
            .HasMaxLength(10);

        builder.Property(r => r.VendorCode)
            .HasMaxLength(30);

        builder.Property(r => r.VendorName)
            .HasMaxLength(100);

        builder.Property(r => r.SourceKey)
            .HasMaxLength(100);

        builder.Property(r => r.RawJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(r => r.ErrorMessage)
            .HasColumnType("nvarchar(max)");

        builder.Property(r => r.ProcessedAt)
            .HasColumnType("datetime2");

        // Indexes
        builder.HasIndex(r => new { r.BranchId, r.GrnNo });
        builder.HasIndex(r => new { r.BranchId, r.SourceKey });
        builder.HasIndex(r => new { r.BranchId, r.Status });
        builder.HasIndex(r => new { r.BranchId, r.Status, r.GrnDate });

        // Relationship
        builder.HasOne(r => r.ApiCredential)
            .WithMany()
            .HasForeignKey(r => r.ApiCredentialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
