using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.BranchId)
            .IsRequired();

        builder.Property(t => t.GrnApiResponseId)
            .IsRequired();

        builder.Property(t => t.TagNo)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Qty)
            .IsRequired();

        builder.Property(t => t.RemainingQty)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.PrintCount)
            .IsRequired();

        builder.Property(t => t.FirstPrintedAt)
            .HasColumnType("datetime2");

        builder.Property(t => t.LastPrintedAt)
            .HasColumnType("datetime2");

        builder.Property(t => t.RowVersion)
            .IsRowVersion();

        // Unique indexes
        builder.HasIndex(t => new { t.BranchId, t.TagNo })
            .IsUnique();

        builder.HasIndex(t => t.GrnApiResponseId)
            .IsUnique();

        // Foreign key to GrnApiResponse (Restrict)
        builder.HasOne(t => t.GrnApiResponse)
            .WithMany()
            .HasForeignKey(t => t.GrnApiResponseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
