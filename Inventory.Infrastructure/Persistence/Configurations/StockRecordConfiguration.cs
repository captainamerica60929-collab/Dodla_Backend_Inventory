using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class StockRecordConfiguration : IEntityTypeConfiguration<StockRecord>
{
    public void Configure(EntityTypeBuilder<StockRecord> builder)
    {
        builder.ToTable("StockRecords");

        builder.HasKey(sr => sr.Id);

        builder.Property(sr => sr.BranchId)
            .IsRequired();

        builder.Property(sr => sr.TagId)
            .IsRequired();

        builder.Property(sr => sr.LocationId)
            .IsRequired();

        builder.Property(sr => sr.MaterialCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(sr => sr.BatchNo)
            .HasMaxLength(50);

        builder.Property(sr => sr.Qty)
            .IsRequired();

        builder.Property(sr => sr.InwardAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(sr => sr.RowVersion)
            .IsRowVersion();

        // Index for FIFO lookup: branch + material + inwardAt
        builder.HasIndex(sr => new { sr.BranchId, sr.MaterialCode, sr.InwardAt });

        // A tag lives in one stock record per branch
        builder.HasIndex(sr => new { sr.BranchId, sr.TagId })
            .IsUnique();

        // Index for location stock summing
        builder.HasIndex(sr => new { sr.BranchId, sr.LocationId });

        builder.HasOne(sr => sr.Tag)
            .WithMany()
            .HasForeignKey(sr => sr.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sr => sr.Location)
            .WithMany()
            .HasForeignKey(sr => sr.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(sr => sr.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
