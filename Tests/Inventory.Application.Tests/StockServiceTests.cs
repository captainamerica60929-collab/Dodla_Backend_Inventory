using Inventory.Application.Common.Interfaces;
using Inventory.Application.Stock.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.Application.Tests;

public class StockServiceTests
{
    private (InventoryDbContext context, StockService stockService, FakeCurrentUser currentUser, FakeClock clock) CreateSut(string dbName, int branchId = 1, int userId = 10)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var currentUser = new FakeCurrentUser { UserId = userId, BranchId = branchId, Role = "Operator" };
        var clock = new FakeClock { UtcNow = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc) };
        var context = new InventoryDbContext(options, currentUser, clock);

        var stockService = new StockService(context, clock, currentUser);
        return (context, stockService, currentUser, clock);
    }

    private async Task<(Tag tag, Location location)> SeedInwardPrerequisitesAsync(
        InventoryDbContext context,
        int branchId = 1,
        string tagNo = "TAG-001",
        string locationCode = "A-01-01",
        int tagQty = 100,
        TagStatus tagStatus = TagStatus.Printed,
        int locationCapacity = 500,
        string materialCode = "MAT-100")
    {
        var branch = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == branchId);
        if (branch == null)
        {
            branch = new Branch { Id = branchId, Code = $"BR{branchId}", Name = $"Branch {branchId}", IsActive = true };
            context.Branches.Add(branch);
            await context.SaveChangesAsync();
        }

        var grn = new GrnApiResponse
        {
            BranchId = branchId,
            GrnNo = "GRN-001",
            MaterialCode = materialCode,
            BatchNo = "BATCH-01",
            Qty = tagQty,
            Status = GrnResponseStatus.Processed
        };
        context.GrnApiResponses.Add(grn);
        await context.SaveChangesAsync();

        var tag = new Tag
        {
            BranchId = branchId,
            GrnApiResponseId = grn.Id,
            TagNo = tagNo,
            Qty = tagQty,
            RemainingQty = tagQty,
            Status = tagStatus,
            PrintCount = tagStatus == TagStatus.Generated ? 0 : 1
        };
        context.Tags.Add(tag);

        var location = new Location
        {
            BranchId = branchId,
            Code = locationCode,
            Zone = "DRY",
            Capacity = locationCapacity,
            IsActive = true
        };
        context.Locations.Add(location);

        await context.SaveChangesAsync();
        return (tag, location);
    }

    [Fact]
    public async Task InwardAsync_ValidPrintedTag_CreatesStockRecordAndMovementAndSetsStatusInStock()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, currentUser, clock) = CreateSut(dbName);
        var (tag, location) = await SeedInwardPrerequisitesAsync(context);

        var request = new InwardScanRequestDto(tag.TagNo, location.Code);
        var result = await stockService.InwardAsync(request, "192.168.1.50");

        Assert.NotNull(result);
        Assert.Equal(tag.TagNo, result.TagNo);
        Assert.Equal(location.Code, result.LocationCode);
        Assert.Equal(100, result.Qty);
        Assert.Equal(100, result.LocationUsedCapacity);

        // Verify Tag status updated
        var updatedTag = await context.Tags.FindAsync(tag.Id);
        Assert.Equal(TagStatus.InStock, updatedTag!.Status);

        // Verify StockRecord created
        var record = await context.StockRecords.FirstOrDefaultAsync(sr => sr.TagId == tag.Id);
        Assert.NotNull(record);
        Assert.Equal(100, record.Qty);
        Assert.Equal(location.Id, record.LocationId);
        Assert.Equal(clock.UtcNow, record.InwardAt);

        // Verify StockMovement recorded
        var movement = await context.StockMovements.FirstOrDefaultAsync(sm => sm.TagId == tag.Id && sm.MovementType == StockMovementType.In);
        Assert.NotNull(movement);
        Assert.Equal(100, movement.Qty);
        Assert.Equal(location.Id, movement.LocationId);
        Assert.Equal(currentUser.UserId, movement.UserId);
        Assert.Equal("192.168.1.50", movement.ClientIp);
        Assert.Equal(clock.UtcNow, movement.InwardAt);
    }

    [Fact]
    public async Task InwardAsync_UnprintedTag_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, location) = await SeedInwardPrerequisitesAsync(context, tagStatus: TagStatus.Generated);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, location.Code), null));

        Assert.Contains("Only printed tags can be inwarded", ex.Message);
    }

    [Fact]
    public async Task InwardAsync_AlreadyInStockTag_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, location) = await SeedInwardPrerequisitesAsync(context, tagStatus: TagStatus.InStock);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, location.Code), null));

        Assert.Contains("already inwarded and in stock", ex.Message);
    }

    [Fact]
    public async Task InwardAsync_ExceedsLocationCapacity_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        // Location capacity is only 50, tag has 100
        var (tag, location) = await SeedInwardPrerequisitesAsync(context, tagQty: 100, locationCapacity: 50);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, location.Code), null));

        Assert.Contains("capacity (50) exceeded", ex.Message);
    }

    [Fact]
    public async Task OutwardAsync_StrictFifo_OlderStockMustBeIssuedFirst()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, clock) = CreateSut(dbName);

        // Seed 2 tags of the same material
        var (oldTag, loc) = await SeedInwardPrerequisitesAsync(context, tagNo: "TAG-OLD", materialCode: "MAT-STEEL", tagQty: 50);
        var (newTag, _) = await SeedInwardPrerequisitesAsync(context, tagNo: "TAG-NEW", locationCode: "A-01-02", materialCode: "MAT-STEEL", tagQty: 50);

        // Inward the old tag at T0
        clock.UtcNow = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        await stockService.InwardAsync(new InwardScanRequestDto(oldTag.TagNo, loc.Code), null);

        // Inward the new tag at T1 (5 days later)
        clock.UtcNow = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        await stockService.InwardAsync(new InwardScanRequestDto(newTag.TagNo, "A-01-02"), null);

        // Attempt to issue the NEW tag first -> MUST fail due to FIFO violation
        clock.UtcNow = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.OutwardAsync(new OutwardIssueRequestDto(newTag.TagNo, 20), null));

        Assert.Contains("FIFO violation", ex.Message);
        Assert.Contains("Older stock for material 'MAT-STEEL' exists", ex.Message);
        Assert.Contains(oldTag.TagNo, ex.Message);

        // Issuing the OLD tag first succeeds
        var oldIssue = await stockService.OutwardAsync(new OutwardIssueRequestDto(oldTag.TagNo, 50), null);
        Assert.Equal(50, oldIssue.IssuedQty);
        Assert.Equal(0, oldIssue.RemainingQty);

        // Now the old tag is exhausted (Qty = 0), issuing the NEW tag succeeds
        var newIssue = await stockService.OutwardAsync(new OutwardIssueRequestDto(newTag.TagNo, 20), null);
        Assert.Equal(20, newIssue.IssuedQty);
        Assert.Equal(30, newIssue.RemainingQty);
    }

    [Fact]
    public async Task OutwardAsync_PartialIssue_PreservesRemainingBalanceOnSameTagEntity()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, currentUser, clock) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context, tagQty: 100);

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, loc.Code), "192.168.1.10");

        // Partial issue: 40 units out of 100
        var issueResult = await stockService.OutwardAsync(new OutwardIssueRequestDto(tag.TagNo, 40), "192.168.1.25");

        Assert.Equal(40, issueResult.IssuedQty);
        Assert.Equal(60, issueResult.RemainingQty);
        Assert.Equal(TagStatus.Issued.ToString(), issueResult.TagStatus);

        // Check StockRecord has remaining 60
        var record = await context.StockRecords.FirstOrDefaultAsync(sr => sr.TagId == tag.Id);
        Assert.NotNull(record);
        Assert.Equal(60, record.Qty);

        // Check Tag entity preserves original Qty and remaining 60
        var updatedTag = await context.Tags.FindAsync(tag.Id);
        Assert.Equal(100, updatedTag!.Qty);
        Assert.Equal(60, updatedTag.RemainingQty);

        // Check OUT movement recorded
        var outMovement = await context.StockMovements
            .FirstOrDefaultAsync(sm => sm.TagId == tag.Id && sm.MovementType == StockMovementType.Out);
        Assert.NotNull(outMovement);
        Assert.Equal(40, outMovement.Qty);
        Assert.Equal(currentUser.UserId, outMovement.UserId);
        Assert.Equal("192.168.1.25", outMovement.ClientIp);
    }

    [Fact]
    public async Task OutwardAsync_OverIssue_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context, tagQty: 30);

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, loc.Code), null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.OutwardAsync(new OutwardIssueRequestDto(tag.TagNo, 50), null));

        Assert.Contains("exceeds available tag balance", ex.Message);
    }

    [Fact]
    public async Task AcceptAsync_IssuedTagWithRemainingBalance_TransitionsBackToInStock()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context, tagQty: 100);

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, loc.Code), null);
        await stockService.OutwardAsync(new OutwardIssueRequestDto(tag.TagNo, 40), null);

        // Accept the partial issuance
        var acceptResult = await stockService.AcceptAsync(new AcceptRequestDto(tag.TagNo), "192.168.1.99");

        Assert.Equal(TagStatus.Issued.ToString(), acceptResult.PreviousStatus);
        // Tag has remaining 60 balance, so it transitions back to InStock
        Assert.Equal(TagStatus.InStock.ToString(), acceptResult.NewStatus);
        Assert.Equal(60, acceptResult.RemainingQty);

        // Verify ACCEPT movement logged
        var acceptMovement = await context.StockMovements
            .FirstOrDefaultAsync(sm => sm.TagId == tag.Id && sm.MovementType == StockMovementType.Accept);
        Assert.NotNull(acceptMovement);
        Assert.Equal(40, acceptMovement.Qty);
        Assert.Equal("192.168.1.99", acceptMovement.ClientIp);
    }

    [Fact]
    public async Task AcceptAsync_FullyIssuedTag_TransitionsToAccepted()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context, tagQty: 100);

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, loc.Code), null);
        await stockService.OutwardAsync(new OutwardIssueRequestDto(tag.TagNo, 100), null);

        var acceptResult = await stockService.AcceptAsync(new AcceptRequestDto(tag.TagNo), null);

        Assert.Equal(TagStatus.Accepted.ToString(), acceptResult.NewStatus);
        Assert.Equal(0, acceptResult.RemainingQty);

        var updatedTag = await context.Tags.FindAsync(tag.Id);
        Assert.Equal(TagStatus.Accepted, updatedTag!.Status);
    }

    [Fact]
    public async Task RejectAsync_IssuedTagWithMandatoryReason_TransitionsToRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context, tagQty: 50);

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, loc.Code), null);
        await stockService.OutwardAsync(new OutwardIssueRequestDto(tag.TagNo, 50), null);

        var rejectResult = await stockService.RejectAsync(
            new RejectRequestDto(tag.TagNo, "Packaging damaged during handling"), "10.0.0.12");

        Assert.Equal(TagStatus.Rejected.ToString(), rejectResult.NewStatus);
        Assert.Equal("Packaging damaged during handling", rejectResult.Reason);

        var updatedTag = await context.Tags.FindAsync(tag.Id);
        Assert.Equal(TagStatus.Rejected, updatedTag!.Status);

        var rejectMovement = await context.StockMovements
            .FirstOrDefaultAsync(sm => sm.TagId == tag.Id && sm.MovementType == StockMovementType.Reject);
        Assert.NotNull(rejectMovement);
        Assert.Equal(50, rejectMovement.Qty);
        Assert.Equal("Packaging damaged during handling", rejectMovement.Reason);
        Assert.Equal("10.0.0.12", rejectMovement.ClientIp);
    }

    [Fact]
    public async Task RejectAsync_MissingReason_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context, tagQty: 50);

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, loc.Code), null);
        await stockService.OutwardAsync(new OutwardIssueRequestDto(tag.TagNo, 50), null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.RejectAsync(new RejectRequestDto(tag.TagNo, "   "), null));

        Assert.Contains("Rejection reason is required", ex.Message);
    }

    [Fact]
    public async Task AcceptOrReject_NonIssuedTag_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context);

        // Tag is still in Printed status
        var acceptEx = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.AcceptAsync(new AcceptRequestDto(tag.TagNo), null));
        Assert.Contains("Only 'Issued' tags can be accepted", acceptEx.Message);

        var rejectEx = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.RejectAsync(new RejectRequestDto(tag.TagNo, "Damaged"), null));
        Assert.Contains("Only 'Issued' tags can be rejected", rejectEx.Message);
    }

    [Fact]
    public async Task BranchScoping_StockRecordsAndMovementsIsolatedByBranch()
    {
        var dbName = Guid.NewGuid().ToString();
        // Branch 1 Sut
        var (context1, stockService1, _, _) = CreateSut(dbName, branchId: 1);
        var (tagB1, locB1) = await SeedInwardPrerequisitesAsync(context1, branchId: 1, tagNo: "TAG-BR1", locationCode: "LOC-B1");
        await stockService1.InwardAsync(new InwardScanRequestDto(tagB1.TagNo, locB1.Code), null);

        // Branch 2 Sut
        var (context2, stockService2, _, _) = CreateSut(dbName, branchId: 2);
        var (tagB2, locB2) = await SeedInwardPrerequisitesAsync(context2, branchId: 2, tagNo: "TAG-BR2", locationCode: "LOC-B2");
        await stockService2.InwardAsync(new InwardScanRequestDto(tagB2.TagNo, locB2.Code), null);

        // Query stock in Branch 1 context
        var branch1Records = await context1.StockRecords.ToListAsync();
        Assert.Single(branch1Records);
        Assert.Equal("TAG-BR1", (await context1.Tags.FindAsync(branch1Records[0].TagId))!.TagNo);

        // Query stock in Branch 2 context
        var branch2Records = await context2.StockRecords.ToListAsync();
        Assert.Single(branch2Records);
        Assert.Equal("TAG-BR2", (await context2.Tags.FindAsync(branch2Records[0].TagId))!.TagNo);
    }

    [Fact]
    public async Task OutwardAsync_WithBypassFifoTrue_AllowsIssuingNewerStockWhenOlderStockExists()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, clock) = CreateSut(dbName);

        // Seed 2 tags of the same material
        var (oldTag, loc) = await SeedInwardPrerequisitesAsync(context, tagNo: "TAG-OLD-BP", materialCode: "MAT-BYPASS", tagQty: 50);
        var (newTag, _) = await SeedInwardPrerequisitesAsync(context, tagNo: "TAG-NEW-BP", locationCode: "A-01-02", materialCode: "MAT-BYPASS", tagQty: 50);

        // Inward old tag at T0
        clock.UtcNow = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        await stockService.InwardAsync(new InwardScanRequestDto(oldTag.TagNo, loc.Code), null);

        // Inward new tag at T1
        clock.UtcNow = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        await stockService.InwardAsync(new InwardScanRequestDto(newTag.TagNo, "A-01-02"), null);

        // Attempt outward on NEW tag with BypassFifo = true -> MUST SUCCEED
        clock.UtcNow = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        var issueResult = await stockService.OutwardAsync(new OutwardIssueRequestDto(newTag.TagNo, 25, BypassFifo: true), null);

        Assert.Equal(25, issueResult.IssuedQty);
        Assert.Equal(25, issueResult.RemainingQty);
        Assert.Equal(TagStatus.Issued.ToString(), issueResult.TagStatus);

        // Verify old tag is still untouched in stock with 50 units
        var oldRecord = await context.StockRecords.FirstOrDefaultAsync(sr => sr.TagId == oldTag.Id);
        Assert.NotNull(oldRecord);
        Assert.Equal(50, oldRecord.Qty);
    }

    [Fact]
    public async Task AcceptAsync_MultiplePartialAcceptCycles_RestoresInStockAndFinallyAccepts()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context, tagQty: 100);

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, loc.Code), null);

        // Cycle 1: Outward 40 -> Accept -> status is InStock with 60 remaining
        var out1 = await stockService.OutwardAsync(new OutwardIssueRequestDto(tag.TagNo, 40), null);
        Assert.Equal(60, out1.RemainingQty);
        var accept1 = await stockService.AcceptAsync(new AcceptRequestDto(tag.TagNo), null);
        Assert.Equal(TagStatus.InStock.ToString(), accept1.NewStatus);
        Assert.Equal(60, accept1.RemainingQty);

        // Cycle 2: Outward 30 -> Accept -> status is InStock with 30 remaining
        var out2 = await stockService.OutwardAsync(new OutwardIssueRequestDto(tag.TagNo, 30), null);
        Assert.Equal(30, out2.RemainingQty);
        var accept2 = await stockService.AcceptAsync(new AcceptRequestDto(tag.TagNo), null);
        Assert.Equal(TagStatus.InStock.ToString(), accept2.NewStatus);
        Assert.Equal(30, accept2.RemainingQty);

        // Cycle 3: Outward 30 (exhausts balance) -> Accept -> status is Accepted with 0 remaining
        var out3 = await stockService.OutwardAsync(new OutwardIssueRequestDto(tag.TagNo, 30), null);
        Assert.Equal(0, out3.RemainingQty);
        var accept3 = await stockService.AcceptAsync(new AcceptRequestDto(tag.TagNo), null);
        Assert.Equal(TagStatus.Accepted.ToString(), accept3.NewStatus);
        Assert.Equal(0, accept3.RemainingQty);

        // Verify final tag state in DB
        var finalTag = await context.Tags.FindAsync(tag.Id);
        Assert.NotNull(finalTag);
        Assert.Equal(TagStatus.Accepted, finalTag.Status);
        Assert.Equal(0, finalTag.RemainingQty);
    }

    [Fact]
    public async Task GetTagStockInfoAsync_AfterPartialIssue_ReturnsDynamicRemainingQtyAsQty()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context, tagQty: 100);

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, loc.Code), null);
        await stockService.OutwardAsync(new OutwardRequestDto(tag.TagNo, 35), null);

        var info = await stockService.GetTagStockInfoAsync(tag.TagNo);

        Assert.NotNull(info);
        // Qty returns remaining balance 65 instead of initial GRN 100
        Assert.Equal(65, info.Qty);
        Assert.Equal(65, info.RemainingQty);
        Assert.Equal(TagStatus.Issued.ToString(), info.Status);
        Assert.Equal(loc.Code, info.LocationCode);
    }

    [Fact]
    public async Task TransferAsync_InStockTag_SuccessfullyMovesToTargetLocationAndLogsMovement()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, currentUser, _) = CreateSut(dbName);
        var (tag, locA) = await SeedInwardPrerequisitesAsync(context, locationCode: "LOC-A", tagQty: 60);

        // Seed destination location LOC-B
        var locB = new Location
        {
            BranchId = 1,
            Code = "LOC-B",
            Zone = "COLD",
            Capacity = 200,
            IsActive = true
        };
        context.Locations.Add(locB);
        await context.SaveChangesAsync();

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, locA.Code), null);

        // Execute transfer from LOC-A to LOC-B
        var transferResult = await stockService.TransferAsync(
            new TransferRequestDto(tag.TagNo, locB.Code), "10.0.0.55");

        Assert.Equal(tag.TagNo, transferResult.TagNo);
        Assert.Equal(60, transferResult.Qty);
        Assert.Equal(locA.Id, transferResult.FromLocationId);
        Assert.Equal(locA.Code, transferResult.FromLocationCode);
        Assert.Equal(locB.Id, transferResult.ToLocationId);
        Assert.Equal(locB.Code, transferResult.ToLocationCode);
        Assert.Equal(60, transferResult.TargetLocationUsedCapacity);

        // Verify StockRecord updated in database
        var record = await context.StockRecords.FirstOrDefaultAsync(sr => sr.TagId == tag.Id);
        Assert.NotNull(record);
        Assert.Equal(locB.Id, record.LocationId);

        // Verify StockMovement logged with Transfer type
        var movement = await context.StockMovements
            .FirstOrDefaultAsync(sm => sm.TagId == tag.Id && sm.MovementType == StockMovementType.Transfer);
        Assert.NotNull(movement);
        Assert.Equal(locB.Id, movement.LocationId);
        Assert.Equal(60, movement.Qty);
        Assert.Equal(currentUser.UserId, movement.UserId);
        Assert.Equal("10.0.0.55", movement.ClientIp);
        Assert.Contains("Transferred from LOC-A to LOC-B", movement.Reason);
    }

    [Fact]
    public async Task TransferAsync_TargetLocationCapacityExceeded_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, locA) = await SeedInwardPrerequisitesAsync(context, locationCode: "LOC-ORIG", tagQty: 80);

        // Seed target location with only 50 capacity
        var locSmall = new Location
        {
            BranchId = 1,
            Code = "LOC-SMALL",
            Capacity = 50,
            IsActive = true
        };
        context.Locations.Add(locSmall);
        await context.SaveChangesAsync();

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, locA.Code), null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.TransferAsync(new TransferRequestDto(tag.TagNo, locSmall.Code), null));

        Assert.Contains("capacity", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exceeded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransferAsync_TagNotInStock_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, _) = await SeedInwardPrerequisitesAsync(context, tagQty: 50);

        var locDest = new Location
        {
            BranchId = 1,
            Code = "LOC-DEST",
            Capacity = 500,
            IsActive = true
        };
        context.Locations.Add(locDest);
        await context.SaveChangesAsync();

        // Tag is in Printed status (not InStock)
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.TransferAsync(new TransferRequestDto(tag.TagNo, locDest.Code), null));

        Assert.Contains("Only 'InStock' tags can be transferred", ex.Message);
    }

    [Fact]
    public async Task TransferAsync_SameLocation_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, loc) = await SeedInwardPrerequisitesAsync(context, locationCode: "LOC-SAME", tagQty: 50);

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, loc.Code), null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.TransferAsync(new TransferRequestDto(tag.TagNo, loc.Code), null));

        Assert.Contains("already located in", ex.Message);
    }

    [Fact]
    public async Task TransferAsync_InactiveTargetLocation_ThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, stockService, _, _) = CreateSut(dbName);
        var (tag, locA) = await SeedInwardPrerequisitesAsync(context, locationCode: "LOC-ACTIVE", tagQty: 50);

        var locInactive = new Location
        {
            BranchId = 1,
            Code = "LOC-INACTIVE",
            Capacity = 500,
            IsActive = false
        };
        context.Locations.Add(locInactive);
        await context.SaveChangesAsync();

        await stockService.InwardAsync(new InwardScanRequestDto(tag.TagNo, locA.Code), null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            stockService.TransferAsync(new TransferRequestDto(tag.TagNo, locInactive.Code), null));

        Assert.Contains("is inactive", ex.Message);
    }
}
