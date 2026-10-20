using System.Data;
using Inventory.Infrastructure.Persistence.Scripts;
using Microsoft.Data.SqlClient;

namespace Inventory.Application.Tests;

/// <summary>
/// Opt-in integration test for the T-SQL stored procedure usp_AllocateTagNumbers.
/// Reads connection string from environment variable TEST_SQL_CONNECTION.
/// Creates a throwaway database with a unique name, tests atomic allocations and rollback behavior,
/// and tears down the database upon completion. Never touches InventoryDb.
/// </summary>
public class SqlServerStoredProcedureIntegrationTests : IAsyncLifetime
{
    private readonly string? _rawMasterConnection;
    private readonly string _testDatabaseName = $"WareTrack_Test_{Guid.NewGuid():N}";
    private string? _testDbConnectionString;

    public SqlServerStoredProcedureIntegrationTests()
    {
        _rawMasterConnection = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION");
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_rawMasterConnection))
            return;

        // Build master connection string
        var builder = new SqlConnectionStringBuilder(_rawMasterConnection)
        {
            InitialCatalog = "master"
        };

        // 1. Create throwaway database
        await using (var conn = new SqlConnection(builder.ConnectionString))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{_testDatabaseName}];";
            await cmd.ExecuteNonQueryAsync();
        }

        // Build connection string targeting the throwaway database
        builder.InitialCatalog = _testDatabaseName;
        _testDbConnectionString = builder.ConnectionString;

        // 2. Create TagCounters schema and usp_AllocateTagNumbers using shared procedure script
        await using (var conn = new SqlConnection(_testDbConnectionString))
        {
            await conn.OpenAsync();

            using var ddlCmd = conn.CreateCommand();
            ddlCmd.CommandText = @"
                CREATE TABLE TagCounters (
                    BranchId INT NOT NULL,
                    [Year] INT NOT NULL,
                    LastNo INT NOT NULL,
                    CONSTRAINT PK_TagCounters PRIMARY KEY (BranchId, [Year])
                );
            ";
            await ddlCmd.ExecuteNonQueryAsync();

            using var spCmd = conn.CreateCommand();
            spCmd.CommandText = StoredProcedureScripts.CreateUspAllocateTagNumbers;
            await spCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_rawMasterConnection))
            return;

        // Force close active connections and drop the throwaway database
        try
        {
            var builder = new SqlConnectionStringBuilder(_rawMasterConnection)
            {
                InitialCatalog = "master"
            };

            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name = '{_testDatabaseName}')
                BEGIN
                    ALTER DATABASE [{_testDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_testDatabaseName}];
                END
            ";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task FirstAllocation_ForNewBranchAndYear_ReturnsOne()
    {
        if (string.IsNullOrWhiteSpace(_testDbConnectionString))
            return; // Skipped when TEST_SQL_CONNECTION is not configured

        const int branchId = 101;
        const int year = 2026;
        const int count = 5;

        await using var conn = new SqlConnection(_testDbConnectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "usp_AllocateTagNumbers";
        cmd.Parameters.Add(new SqlParameter("@BranchId", branchId));
        cmd.Parameters.Add(new SqlParameter("@Year", year));
        cmd.Parameters.Add(new SqlParameter("@Count", count));
        var pFirstNo = new SqlParameter("@FirstNo", SqlDbType.Int) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(pFirstNo);

        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();

        Assert.Equal(1, (int)pFirstNo.Value);

        // Verify TagCounters row has LastNo = count (5)
        using var verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT LastNo FROM TagCounters WHERE BranchId = @BranchId AND [Year] = @Year;";
        verifyCmd.Parameters.Add(new SqlParameter("@BranchId", branchId));
        verifyCmd.Parameters.Add(new SqlParameter("@Year", year));
        var lastNo = (int)(await verifyCmd.ExecuteScalarAsync())!;
        Assert.Equal(count, lastNo);
    }

    [Fact]
    public async Task ParallelAllocations_20Threads_CountOne_Return1To20_WithNoDuplicatesAndNoDeadlock()
    {
        if (string.IsNullOrWhiteSpace(_testDbConnectionString))
            return; // Skipped when TEST_SQL_CONNECTION is not configured

        const int branchId = 102;
        const int year = 2026;
        const int threadCount = 20;

        var tasks = Enumerable.Range(0, threadCount).Select(async _ =>
        {
            await using var conn = new SqlConnection(_testDbConnectionString);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "usp_AllocateTagNumbers";
            cmd.Parameters.Add(new SqlParameter("@BranchId", branchId));
            cmd.Parameters.Add(new SqlParameter("@Year", year));
            cmd.Parameters.Add(new SqlParameter("@Count", 1));
            var pFirstNo = new SqlParameter("@FirstNo", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(pFirstNo);

            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();

            return (int)pFirstNo.Value;
        }).ToList();

        var allocatedNumbers = (await Task.WhenAll(tasks)).ToList();

        // 20 allocations of count 1
        Assert.Equal(threadCount, allocatedNumbers.Count);

        // No duplicates
        Assert.Equal(threadCount, allocatedNumbers.Distinct().Count());

        // Return 1..20
        allocatedNumbers.Sort();
        var expected = Enumerable.Range(1, 20).ToList();
        Assert.Equal(expected, allocatedNumbers);
    }

    [Fact]
    public async Task OverflowNear999999_ThrowsAndLeavesCounterUnchanged()
    {
        if (string.IsNullOrWhiteSpace(_testDbConnectionString))
            return; // Skipped when TEST_SQL_CONNECTION is not configured

        const int branchId = 103;
        const int year = 2026;
        const int seedLastNo = 999995;

        // Seed counter near max
        await using (var conn = new SqlConnection(_testDbConnectionString))
        {
            await conn.OpenAsync();
            using var seedCmd = conn.CreateCommand();
            seedCmd.CommandText = "INSERT INTO TagCounters (BranchId, [Year], LastNo) VALUES (@BranchId, @Year, @LastNo);";
            seedCmd.Parameters.Add(new SqlParameter("@BranchId", branchId));
            seedCmd.Parameters.Add(new SqlParameter("@Year", year));
            seedCmd.Parameters.Add(new SqlParameter("@LastNo", seedLastNo));
            await seedCmd.ExecuteNonQueryAsync();
        }

        // Attempt allocation of 10 (999995 + 10 = 1000005 > 999999) inside transaction
        await using (var conn = new SqlConnection(_testDbConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "usp_AllocateTagNumbers";
            cmd.Parameters.Add(new SqlParameter("@BranchId", branchId));
            cmd.Parameters.Add(new SqlParameter("@Year", year));
            cmd.Parameters.Add(new SqlParameter("@Count", 10));
            var pFirstNo = new SqlParameter("@FirstNo", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(pFirstNo);

            var ex = await Assert.ThrowsAsync<SqlException>(async () => await cmd.ExecuteNonQueryAsync());
            Assert.Contains("Tag sequence counter exceeded the maximum of 999999.", ex.Message);

            // Since it threw under XACT_ABORT ON, transaction is doomed / aborted
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
                // Transaction may already be aborted
            }
        }

        // Verify counter is unchanged
        await using (var conn = new SqlConnection(_testDbConnectionString))
        {
            await conn.OpenAsync();
            using var verifyCmd = conn.CreateCommand();
            verifyCmd.CommandText = "SELECT LastNo FROM TagCounters WHERE BranchId = @BranchId AND [Year] = @Year;";
            verifyCmd.Parameters.Add(new SqlParameter("@BranchId", branchId));
            verifyCmd.Parameters.Add(new SqlParameter("@Year", year));
            var currentLastNo = (int)(await verifyCmd.ExecuteScalarAsync())!;
            Assert.Equal(seedLastNo, currentLastNo);
        }
    }

    [Fact]
    public async Task CallOutsideTransaction_ThrowsException()
    {
        if (string.IsNullOrWhiteSpace(_testDbConnectionString))
            return; // Skipped when TEST_SQL_CONNECTION is not configured

        const int branchId = 104;
        const int year = 2026;

        await using var conn = new SqlConnection(_testDbConnectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "usp_AllocateTagNumbers";
        cmd.Parameters.Add(new SqlParameter("@BranchId", branchId));
        cmd.Parameters.Add(new SqlParameter("@Year", year));
        cmd.Parameters.Add(new SqlParameter("@Count", 1));
        var pFirstNo = new SqlParameter("@FirstNo", SqlDbType.Int) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(pFirstNo);

        var ex = await Assert.ThrowsAsync<SqlException>(async () => await cmd.ExecuteNonQueryAsync());
        Assert.Contains("usp_AllocateTagNumbers must run inside a transaction.", ex.Message);
    }

    [Fact]
    public async Task RollbackLeavesNoGap_AndSubsequentAllocationReusesRange()
    {
        if (string.IsNullOrWhiteSpace(_testDbConnectionString))
            return; // Skipped when TEST_SQL_CONNECTION is not configured

        const int branchId = 105;
        const int year = 2026;

        // 1. Allocate in transaction and roll back
        await using (var conn = new SqlConnection(_testDbConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "usp_AllocateTagNumbers";
            cmd.Parameters.Add(new SqlParameter("@BranchId", branchId));
            cmd.Parameters.Add(new SqlParameter("@Year", year));
            cmd.Parameters.Add(new SqlParameter("@Count", 5));
            var pFirstNo = new SqlParameter("@FirstNo", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(pFirstNo);

            await cmd.ExecuteNonQueryAsync();
            Assert.Equal(1, (int)pFirstNo.Value);

            // Explicitly roll back
            await tx.RollbackAsync();
        }

        // 2. Next allocation must start at 1 because the rollback left no gap
        await using (var conn = new SqlConnection(_testDbConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "usp_AllocateTagNumbers";
            cmd.Parameters.Add(new SqlParameter("@BranchId", branchId));
            cmd.Parameters.Add(new SqlParameter("@Year", year));
            cmd.Parameters.Add(new SqlParameter("@Count", 3));
            var pFirstNo = new SqlParameter("@FirstNo", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(pFirstNo);

            await cmd.ExecuteNonQueryAsync();
            Assert.Equal(1, (int)pFirstNo.Value); // Reused 1 because of rollback!

            await tx.CommitAsync();
        }
    }
}
