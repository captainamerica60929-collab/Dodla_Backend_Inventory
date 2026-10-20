using Inventory.Domain.Common;

namespace Inventory.Domain.Tests;

public class TagNumberFormatterTests
{
    [Fact]
    public void Format_WithStandardInputs_ReturnsExpectedTag()
    {
        // Act
        var result = TagNumberFormatter.Format("MAIN", 2026, 1);

        // Assert
        Assert.Equal("MAIN-26-000001", result);
    }

    [Fact]
    public void Format_WithDateTimeOverload_ExtractsYearCorrectly()
    {
        // Arrange
        var date = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var result = TagNumberFormatter.Format("MAIN", date, 42);

        // Assert
        Assert.Equal("MAIN-26-000042", result);
    }

    [Fact]
    public void Format_WithHyphenatedBranchCode_PreservesHyphenInBranch()
    {
        // Act
        var result = TagNumberFormatter.Format("MA-IN", 2026, 1);

        // Assert
        Assert.Equal("MA-IN-26-000001", result);
    }

    [Fact]
    public void Format_WithMaxSequenceNumber_FormatsSuccessfully()
    {
        // Act
        var result = TagNumberFormatter.Format("MAIN", 2026, 999999);

        // Assert
        Assert.Equal("MAIN-26-999999", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000000)]
    public void Format_WithInvalidSequenceNumber_ThrowsArgumentOutOfRangeException(int invalidSeq)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TagNumberFormatter.Format("MAIN", 2026, invalidSeq));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_WithNullOrWhitespaceBranchCode_ThrowsArgumentException(string? invalidBranch)
    {
        Assert.Throws<ArgumentException>(() => TagNumberFormatter.Format(invalidBranch!, 2026, 1));
    }

    [Fact]
    public void TryParse_WithStandardTag_ParsesSuccessfully()
    {
        // Act
        var success = TagNumberFormatter.TryParse("MAIN-26-000001", out var branch, out var year, out var seq);

        // Assert
        Assert.True(success);
        Assert.Equal("MAIN", branch);
        Assert.Equal(2026, year);
        Assert.Equal(1, seq);
    }

    [Fact]
    public void TryParse_WithHyphenatedBranchCode_SplitsFromRightCorrectly()
    {
        // Act: Branch code has a hyphen
        var success = TagNumberFormatter.TryParse("MA-IN-26-000001", out var branch, out var year, out var seq);

        // Assert
        Assert.True(success);
        Assert.Equal("MA-IN", branch);
        Assert.Equal(2026, year);
        Assert.Equal(1, seq);
    }

    [Fact]
    public void TryParse_WithMultiHyphenBranchCode_SplitsFromRightCorrectly()
    {
        // Act: Branch code has multiple hyphens
        var success = TagNumberFormatter.TryParse("PLANT-SOUTH-A-26-000123", out var branch, out var year, out var seq);

        // Assert
        Assert.True(success);
        Assert.Equal("PLANT-SOUTH-A", branch);
        Assert.Equal(2026, year);
        Assert.Equal(123, seq);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MAIN-26")]
    [InlineData("MAIN-2026-000001")] // 4-digit year instead of 2
    [InlineData("MAIN-26-0001")] // 4-digit seq instead of 6
    [InlineData("MAIN-26-000000")] // Seq 0 is invalid
    [InlineData("MAIN-26-1000000")] // 7 digits
    [InlineData("MAIN-26-ABCDEF")]
    [InlineData("-26-000001")] // Empty branch
    public void TryParse_WithInvalidTag_ReturnsFalseAndResetsOutParams(string? invalidTag)
    {
        // Act
        var success = TagNumberFormatter.TryParse(invalidTag, out var branch, out var year, out var seq);

        // Assert
        Assert.False(success);
        Assert.Equal(string.Empty, branch);
        Assert.Equal(0, year);
        Assert.Equal(0, seq);
    }
}
