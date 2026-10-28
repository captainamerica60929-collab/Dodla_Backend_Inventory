using System.IO;
using ClosedXML.Excel;
using Inventory.Domain.Exceptions;

namespace Inventory.Infrastructure.Services;

public static class ExcelImportHelper
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public static XLWorkbook LoadAndValidateWorkbook(Stream stream, string fileName)
    {
        if (stream == null || stream.Length == 0)
            throw new DomainException("The uploaded file is empty.");

        var ext = Path.GetExtension(fileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("The uploaded file must have a .xlsx extension.");

        if (stream.Length > 2 * 1024 * 1024)
            throw new DomainException("The uploaded file exceeds the 2 MB size limit.");

        // Validate zip signature (magic bytes: PK\x03\x04)
        var buffer = new byte[4];
        var originalPos = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        var bytesRead = stream.Read(buffer, 0, 4);
        if (stream.CanSeek)
            stream.Position = originalPos;

        if (bytesRead < 4 || !buffer.SequenceEqual(ZipSignature))
            throw new DomainException("The uploaded file is not a valid Excel (.xlsx) file.");

        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            throw new DomainException("The Excel file is corrupt or unreadable.");
        }
    }

    public static string GetCellText(IXLCell cell)
    {
        if (cell == null || cell.IsEmpty())
            return string.Empty;

        if (cell.HasFormula)
        {
            try
            {
                // Read pre-calculated cached value without recalculation
                var cached = cell.CachedValue.ToString().Trim();
                if (!string.IsNullOrEmpty(cached))
                    return cached;
            }
            catch
            {
            }

            try
            {
                return cell.Value.ToString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        try
        {
            return cell.Value.ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
