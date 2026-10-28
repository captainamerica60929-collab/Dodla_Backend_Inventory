using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class TagPrintLogConfiguration : IEntityTypeConfiguration<TagPrintLog>
{
    public void Configure(EntityTypeBuilder<TagPrintLog> builder)
    {
        builder.ToTable("TagPrintLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.TagId)
            .IsRequired();

        builder.Property(l => l.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(l => l.Reason)
            .HasMaxLength(200);

        builder.Property(l => l.PrintedAt)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(l => l.UserId)
            .IsRequired();

        builder.Property(l => l.UserNameSnapshot)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.ClientIp)
            .HasMaxLength(50);

        // Index on TagId and UserId
        builder.HasIndex(l => l.TagId);
        builder.HasIndex(l => l.UserId);

        // Foreign key to Tag (Restrict)
        builder.HasOne(l => l.Tag)
            .WithMany(t => t.PrintLogs)
            .HasForeignKey(l => l.TagId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
