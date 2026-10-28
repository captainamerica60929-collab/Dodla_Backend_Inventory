using Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

public class TagCounterConfiguration : IEntityTypeConfiguration<TagCounter>
{
    public void Configure(EntityTypeBuilder<TagCounter> builder)
    {
        builder.ToTable("TagCounters");

        builder.HasKey(c => new { c.BranchId, c.Year });

        builder.Property(c => c.BranchId)
            .IsRequired();

        builder.Property(c => c.Year)
            .IsRequired();

        builder.Property(c => c.LastNo)
            .IsRequired();
    }
}
