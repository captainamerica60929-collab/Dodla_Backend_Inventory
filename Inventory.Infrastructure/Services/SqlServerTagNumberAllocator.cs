using System.Data;
using Inventory.Application.Common.Interfaces;
using Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// Production allocator that executes the stored procedure usp_AllocateTagNumbers
/// on the active DbContext transaction and connection.
/// </summary>
public class SqlServerTagNumberAllocator : ITagNumberAllocator
{
    private readonly InventoryDbContext _context;

    public SqlServerTagNumberAllocator(InventoryDbContext context)
    {
        _context = context;
    }

    public async Task<int> AllocateAsync(int branchId, int year, int count, CancellationToken ct = default)
    {
        var pBranchId = new SqlParameter("@BranchId", SqlDbType.Int) { Value = branchId };
        var pYear = new SqlParameter("@Year", SqlDbType.Int) { Value = year };
        var pCount = new SqlParameter("@Count", SqlDbType.Int) { Value = count };
        var pFirstNo = new SqlParameter("@FirstNo", SqlDbType.Int) { Direction = ParameterDirection.Output };

        await _context.Database.ExecuteSqlRawAsync(
            "EXEC usp_AllocateTagNumbers @BranchId, @Year, @Count, @FirstNo OUTPUT",
            new object[] { pBranchId, pYear, pCount, pFirstNo },
            ct);

        if (pFirstNo.Value is int firstNo)
        {
            return firstNo;
        }

        throw new InvalidOperationException("Stored procedure usp_AllocateTagNumbers did not return an output sequence number.");
    }
}
