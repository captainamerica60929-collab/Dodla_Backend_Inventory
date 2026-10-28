namespace Inventory.Infrastructure.Persistence.Scripts;

public static class StoredProcedureScripts
{
    public const string DropUspAllocateTagNumbers = "DROP PROCEDURE IF EXISTS usp_AllocateTagNumbers;";

    public const string CreateUspAllocateTagNumbers = @"
CREATE PROCEDURE usp_AllocateTagNumbers
    @BranchId INT,
    @Year INT,
    @Count INT,
    @FirstNo INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @@TRANCOUNT = 0
    BEGIN
        THROW 50001, 'usp_AllocateTagNumbers must run inside a transaction.', 1;
    END;

    IF @Count < 1 OR @Count > 999999
    BEGIN
        THROW 50002, '@Count must be between 1 and 999999.', 1;
    END;

    INSERT INTO TagCounters (BranchId, [Year], LastNo)
    SELECT @BranchId, @Year, 0
    WHERE NOT EXISTS (
        SELECT 1 FROM TagCounters WITH (UPDLOCK, HOLDLOCK)
        WHERE BranchId = @BranchId AND [Year] = @Year
    );

    DECLARE @Prev INT;
    UPDATE TagCounters
    SET @Prev = LastNo,
        LastNo = LastNo + @Count
    WHERE BranchId = @BranchId AND [Year] = @Year AND LastNo + @Count <= 999999;

    IF @@ROWCOUNT = 0
    BEGIN
        THROW 50003, 'Tag sequence counter exceeded the maximum of 999999.', 1;
    END;

    SET @FirstNo = @Prev + 1;
END;";
}
