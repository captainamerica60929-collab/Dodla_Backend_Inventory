using System.Security.Claims;
using Inventory.Application.Common.Interfaces;
using Inventory.Application.Tags.Services;
using Inventory.Application.Tests.Fakes;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Tests;

public class TagServiceTests
{
    private readonly InventoryDbContext _context;
    private readonly FakeTagNumberAllocator _allocator = new();
    private readonly TestClock _clock = new(new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc));
    private readonly TestCurrentUser _currentUser = new() { UserId = 1, BranchId = 1, Role = "StoreKeeper" };
    private readonly HttpContextAccessor _httpContextAccessor = new();
    private readonly TagService _service;

    public TagServiceTests()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new InventoryDbContext(options, _currentUser, _clock);

        var httpContext = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", "1"),
            new Claim("name", "Store Operator Snapshot")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _httpContextAccessor.HttpContext = httpContext;

        _service = new TagService(_context, _allocator, _clock, _currentUser, _httpContextAccessor);

        // Seed branch
        _context.Branches.Add(new Branch
        {
            Id = 1,
            Code = "MAIN",
            Name = "Main Branch",
            IsActive = true
        });
        _context.SaveChanges();
    }

    [Fact]
    public async Task PrepareTagAsync_WhenValidRow_CreatesTagAndSetsStatusProcessed()
    {
        // Arrange
        var grnRow = new GrnApiResponse
        {
            BranchId = 1,
            GrnNo = "GRN-100",
            GrnDate = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            Qty = 50m,
            Status = GrnResponseStatus.New
        };
        _context.GrnApiResponses.Add(grnRow);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.PrepareTagAsync(grnRow.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MAIN-26-000001", result.TagNo);
        Assert.Equal(50, result.Qty);
        Assert.Equal("Generated", result.Status);
        Assert.Equal(0, result.PrintCount);

        // Verify GRN row updated
        var updatedRow = await _context.GrnApiResponses.FindAsync(grnRow.Id);
        Assert.Equal(GrnResponseStatus.Processed, updatedRow!.Status);
        Assert.NotNull(updatedRow.ProcessedAt);
    }

    [Fact]
    public async Task PrepareTagAsync_WhenCalledTwice_IsIdempotentAndReturnsSameTag()
    {
        // Arrange
        var grnRow = new GrnApiResponse
        {
            BranchId = 1,
            GrnNo = "GRN-200",
            Qty = 10m,
            Status = GrnResponseStatus.New
        };
        _context.GrnApiResponses.Add(grnRow);
        await _context.SaveChangesAsync();

        // Act
        var first = await _service.PrepareTagAsync(grnRow.Id);
        var second = await _service.PrepareTagAsync(grnRow.Id);

        // Assert
        Assert.Equal(first.TagId, second.TagId);
        Assert.Equal(first.TagNo, second.TagNo);
        Assert.Equal(1, await _context.Tags.CountAsync());
    }

    [Fact]
    public async Task PrepareTagAsync_WhenRowStatusIsError_ThrowsDomainException()
    {
        // Arrange
        var grnRow = new GrnApiResponse
        {
            BranchId = 1,
            GrnNo = "GRN-ERR",
            Qty = 10m,
            Status = GrnResponseStatus.Error,
            ErrorMessage = "Invalid SAP row"
        };
        _context.GrnApiResponses.Add(grnRow);
        await _context.SaveChangesAsync();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.PrepareTagAsync(grnRow.Id));
        Assert.Contains("status Error", ex.Message);
    }

    [Theory]
    [InlineData("10.5")]
    [InlineData("0.0")]
    [InlineData("-5.0")]
    public async Task PrepareTagAsync_WhenQuantityIsFractionalOrNonPositive_ThrowsDomainException(string qtyStr)
    {
        // Arrange
        var qty = decimal.Parse(qtyStr);
        var grnRow = new GrnApiResponse
        {
            BranchId = 1,
            GrnNo = "GRN-QTY",
            Qty = qty,
            Status = GrnResponseStatus.New
        };
        _context.GrnApiResponses.Add(grnRow);
        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<DomainException>(() => _service.PrepareTagAsync(grnRow.Id));
    }

    private async Task<Tag> CreateTagWithGrnAsync(string grnNo, string tagNo, TagStatus status, int printCount)
    {
        var grn = new GrnApiResponse
        {
            BranchId = 1,
            GrnNo = grnNo,
            Qty = 100m,
            Status = GrnResponseStatus.Processed
        };
        _context.GrnApiResponses.Add(grn);
        await _context.SaveChangesAsync();

        var tag = new Tag
        {
            BranchId = 1,
            GrnApiResponseId = grn.Id,
            GrnApiResponse = grn,
            TagNo = tagNo,
            Qty = 100,
            Status = status,
            PrintCount = printCount,
            FirstPrintedAt = printCount > 0 ? DateTime.UtcNow : null,
            LastPrintedAt = printCount > 0 ? DateTime.UtcNow : null
        };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();
        return tag;
    }

    [Fact]
    public async Task MarkPrintedAsync_FirstTime_SetsPrintedStatusIncrementsCountAndLogsAudit()
    {
        // Arrange
        var tag = await CreateTagWithGrnAsync("GRN-P1", "MAIN-26-000001", TagStatus.Generated, 0);

        // Act
        var result = await _service.MarkPrintedAsync(tag.Id, "192.168.1.50");

        // Assert
        Assert.Equal("Printed", result.Status);
        Assert.Equal(1, result.PrintCount);

        var log = await _context.TagPrintLogs.FirstOrDefaultAsync(l => l.TagId == tag.Id);
        Assert.NotNull(log);
        Assert.Equal(TagPrintKind.Print, log.Kind);
        Assert.Null(log.Reason);
        Assert.Equal(1, log.UserId);
        Assert.Equal("Store Operator Snapshot", log.UserNameSnapshot);
        Assert.Equal("192.168.1.50", log.ClientIp);
    }

    [Fact]
    public async Task ReprintAsync_WhenPrintCountZero_ThrowsDomainExceptionWithExactMessage()
    {
        // Arrange
        var tag = await CreateTagWithGrnAsync("GRN-P2", "MAIN-26-000002", TagStatus.Generated, 0);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.ReprintAsync(tag.Id, "Damaged sticker", "127.0.0.1"));
        Assert.Equal("This tag has not been printed yet. Use the printed call after printing.", ex.Message);
    }

    [Fact]
    public async Task ReprintAsync_WhenPrinted_IncrementsPrintCountAndWritesReprintLog()
    {
        // Arrange
        var tag = await CreateTagWithGrnAsync("GRN-P3", "MAIN-26-000003", TagStatus.Printed, 1);

        // Act
        var result = await _service.ReprintAsync(tag.Id, "Barcode damaged during transit", "10.0.0.5");

        // Assert
        Assert.Equal(2, result.PrintCount);

        var log = await _context.TagPrintLogs
            .Where(l => l.TagId == tag.Id && l.Kind == TagPrintKind.Reprint)
            .FirstOrDefaultAsync();

        Assert.NotNull(log);
        Assert.Equal("Barcode damaged during transit", log.Reason);
        Assert.Equal("10.0.0.5", log.ClientIp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReprintAsync_WhenReasonMissingOrBlank_ThrowsDomainException(string? invalidReason)
    {
        // Arrange
        var tag = await CreateTagWithGrnAsync("GRN-P4", "MAIN-26-000004", TagStatus.Printed, 1);

        // Act & Assert
        await Assert.ThrowsAsync<DomainException>(() => _service.ReprintAsync(tag.Id, invalidReason!, "127.0.0.1"));
    }

    [Fact]
    public async Task MarkPrintedAsync_WhenTagIsVoid_ThrowsDomainException()
    {
        // Arrange
        var tag = await CreateTagWithGrnAsync("GRN-P5", "MAIN-26-000005", TagStatus.Void, 1);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.MarkPrintedAsync(tag.Id, null));
        Assert.Contains("void", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetGrnPrintRowsAsync_AppliesPrintStateFiltersCorrectly()
    {
        // Arrange
        var row1 = new GrnApiResponse { BranchId = 1, GrnNo = "G1", Qty = 10, Status = GrnResponseStatus.New };
        var row2 = new GrnApiResponse { BranchId = 1, GrnNo = "G2", Qty = 20, Status = GrnResponseStatus.Processed };
        var row3 = new GrnApiResponse { BranchId = 1, GrnNo = "G3", Qty = 30, Status = GrnResponseStatus.Processed };
        var rowErr = new GrnApiResponse { BranchId = 1, GrnNo = "G4", Qty = 40, Status = GrnResponseStatus.Error };
        _context.GrnApiResponses.AddRange(row1, row2, row3, rowErr);
        await _context.SaveChangesAsync();

        // row2 has a tag with PrintCount = 0 (Prepared)
        _context.Tags.Add(new Tag { BranchId = 1, GrnApiResponseId = row2.Id, GrnApiResponse = row2, TagNo = "MAIN-26-000001", Qty = 20, PrintCount = 0, Status = TagStatus.Generated });
        // row3 has a tag with PrintCount = 1 (Printed)
        _context.Tags.Add(new Tag { BranchId = 1, GrnApiResponseId = row3.Id, GrnApiResponse = row3, TagNo = "MAIN-26-000002", Qty = 30, PrintCount = 1, Status = TagStatus.Printed });
        await _context.SaveChangesAsync();

        // Act & Assert
        var all = await _service.GetGrnPrintRowsAsync(PrintStateFilter.All, null, null, null, 1, 50);
        Assert.Equal(3, all.TotalCount); // Excludes Error

        var notPrepared = await _service.GetGrnPrintRowsAsync(PrintStateFilter.NotPrepared, null, null, null, 1, 50);
        Assert.Single(notPrepared.Items);
        Assert.Equal("G1", notPrepared.Items[0].GrnNo);

        var prepared = await _service.GetGrnPrintRowsAsync(PrintStateFilter.Prepared, null, null, null, 1, 50);
        Assert.Single(prepared.Items);
        Assert.Equal("G2", prepared.Items[0].GrnNo);

        var printed = await _service.GetGrnPrintRowsAsync(PrintStateFilter.Printed, null, null, null, 1, 50);
        Assert.Single(printed.Items);
        Assert.Equal("G3", printed.Items[0].GrnNo);
    }

    [Fact]
    public async Task GetGrnPrintRowsAsync_FormatsGrnDateWithoutTrailingZ()
    {
        // Arrange
        var testDate = new DateTime(2026, 10, 15, 14, 30, 45);
        var row = new GrnApiResponse
        {
            BranchId = 1,
            GrnNo = "G-FORMAT-TEST",
            GrnDate = testDate,
            Qty = 10,
            Status = GrnResponseStatus.New
        };
        _context.GrnApiResponses.Add(row);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetGrnPrintRowsAsync(PrintStateFilter.All, "G-FORMAT-TEST", null, null, 1, 10);

        // Assert
        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal("2026-10-15T14:30:45", item.GrnDate);
        Assert.False(item.GrnDate!.EndsWith("Z", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrepareTagAsync_WhenConcurrentRaceViolatesUniqueIndex_RollsBackAndReturnsWinningTag()
    {
        // Arrange
        var grnRow = new GrnApiResponse
        {
            BranchId = 1,
            GrnNo = "GRN-RACE",
            Qty = 15m,
            Status = GrnResponseStatus.New
        };
        _context.GrnApiResponses.Add(grnRow);
        await _context.SaveChangesAsync();

        var raceAllocator = new RaceSimulatingAllocator(_context, grnRow);
        var raceService = new TagService(_context, raceAllocator, _clock, _currentUser, _httpContextAccessor);

        // Act
        var result = await raceService.PrepareTagAsync(grnRow.Id);

        // Assert: returned the winning tag that won the race
        Assert.NotNull(result);
        Assert.Equal("MAIN-26-000999", result.TagNo);
    }

    private class RaceSimulatingAllocator : ITagNumberAllocator
    {
        private readonly InventoryDbContext _context;
        private readonly GrnApiResponse _row;

        public RaceSimulatingAllocator(InventoryDbContext context, GrnApiResponse row)
        {
            _context = context;
            _row = row;
        }

        public Task<int> AllocateAsync(int branchId, int year, int count, CancellationToken ct = default)
        {
            var winnerTag = new Tag
            {
                BranchId = branchId,
                GrnApiResponseId = _row.Id,
                GrnApiResponse = _row,
                TagNo = "MAIN-26-000999",
                Qty = (int)_row.Qty!.Value,
                Status = TagStatus.Generated,
                PrintCount = 0
            };
            _context.Tags.Add(winnerTag);
            _context.SaveChanges();

            throw new DbUpdateException("Cannot insert duplicate key in object with unique index 'IX_Tags_GrnApiResponseId'.");
        }
    }

    private class TestCurrentUser : ICurrentUser
    {
        public int UserId { get; set; }
        public int BranchId { get; set; }
        public string Role { get; set; } = string.Empty;
        public bool IsSystem => false;
    }

    private class TestClock : IClock
    {
        public DateTime UtcNow { get; }
        public TestClock(DateTime utcNow) => UtcNow = utcNow;
    }
}
