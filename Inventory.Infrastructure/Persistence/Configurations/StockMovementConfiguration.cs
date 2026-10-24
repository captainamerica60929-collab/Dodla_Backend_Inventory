using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");

        builder.HasKey(sm => sm.Id);

        builder.Property(sm => sm.BranchId)
            .IsRequired();

        builder.Property(sm => sm.TagId)
            .IsRequired();

        builder.Property(sm => sm.LocationId)
            .IsRequired(false);

        builder.Property(sm => sm.MaterialCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(sm => sm.MovementType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(sm => sm.Qty)
            .IsRequired();

        builder.Property(sm => sm.InwardAt)
            .HasColumnType("datetime2")
            .IsRequired(false);

        builder.Property(sm => sm.Reason)
            .HasMaxLength(250);

        builder.Property(sm => sm.UserId)
            .IsRequired();

        builder.Property(sm => sm.UserNameSnapshot)
            .HasMaxLength(100);

        builder.Property(sm => sm.ClientIp)
            .HasMaxLength(50);

        builder.HasIndex(sm => new { sm.BranchId, sm.CreatedAt });
        builder.HasIndex(sm => sm.TagId);
        builder.HasIndex(sm => sm.LocationId);

        builder.HasOne(sm => sm.Tag)
            .WithMany()
            .HasForeignKey(sm => sm.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sm => sm.Location)
            .WithMany()
            .HasForeignKey(sm => sm.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(sm => sm.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
