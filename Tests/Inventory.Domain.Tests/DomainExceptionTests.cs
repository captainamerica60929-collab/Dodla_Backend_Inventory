using Inventory.Domain.Common;
using Inventory.Domain.Exceptions;

namespace Inventory.Domain.Tests;

public class DomainExceptionTests
{
    [Fact]
    public void DomainException_ShouldStoreMessageCorrectly()
    {
        // Arrange
        const string expectedMessage = "Cannot transfer stock from empty location";

        // Act
        var exception = new DomainException(expectedMessage);

        // Assert
        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public void DomainException_WithInnerException_ShouldPreserveInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner failure");
        const string expectedMessage = "Domain rule violation";

        // Act
        var exception = new DomainException(expectedMessage, innerException);

        // Assert
        Assert.Equal(expectedMessage, exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    private class TestBranchEntity : BranchScopedEntity
    {
    }

    [Fact]
    public void BranchScopedEntity_ShouldInheritBaseEntityAndTrackProperties()
    {
        // Arrange & Act
        var entity = new TestBranchEntity
        {
            Id = 42,
            BranchId = 5,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = 101,
            ModifiedAt = DateTime.UtcNow,
            ModifiedBy = 102
        };

        // Assert
        Assert.Equal(42, entity.Id);
        Assert.Equal(5, entity.BranchId);
        Assert.Equal(101, entity.CreatedBy);
        Assert.Equal(102, entity.ModifiedBy);
        Assert.NotNull(entity.ModifiedAt);
    }
}
