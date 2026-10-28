using System.Globalization;

namespace Inventory.Domain.Common;

/// <summary>
/// Formats tag numbers (stickers / barcode tags) from branch code, year, and sequence counter.
/// Isolates the tag string formatting so format patterns can change later without modifying
/// the underlying sequence counter table or stored procedure.
/// Standard agreed format: {BranchCode}-{YY}-{counter:D6} (e.g. MAIN-26-000001 or MA-IN-26-000001).
/// </summary>
public static class TagNumberFormatter
{
    public const int DefaultSequencePadding = 6;
    public const int MaxSequenceNumber = 999999;

    /// <summary>
    /// Formats a tag number using branch code, year, and sequence number.
    /// Pattern: {BranchCode}-{YY}-{counter:D6} (e.g. MAIN-26-000001).
    /// </summary>
    public static string Format(string branchCode, int year, int sequenceNumber)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ArgumentException("Branch code cannot be null or whitespace.", nameof(branchCode));

        if (sequenceNumber < 1 || sequenceNumber > MaxSequenceNumber)
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber),
                $"Sequence number must be between 1 and {MaxSequenceNumber}. Value was {sequenceNumber}.");

        var cleanBranch = branchCode.Trim().ToUpperInvariant();
        var yy = (Math.Abs(year) % 100).ToString("D2", CultureInfo.InvariantCulture);
        var seq = sequenceNumber.ToString("D6", CultureInfo.InvariantCulture);

        return $"{cleanBranch}-{yy}-{seq}";
    }

    /// <summary>
    /// Overload taking a DateTime timestamp. Uses the date's year.
    /// </summary>
    public static string Format(string branchCode, DateTime date, int sequenceNumber)
        => Format(branchCode, date.Year, sequenceNumber);

    /// <summary>
    /// Attempts to parse a formatted tag number into its branch code, year, and sequence number components.
    /// Splits from the right to safely accommodate branch codes that contain hyphens (e.g. MA-IN-26-000001).
    /// </summary>
    public static bool TryParse(string? tagNo, out string branchCode, out int year, out int sequenceNumber)
    {
        branchCode = string.Empty;
        year = 0;
        sequenceNumber = 0;

        if (string.IsNullOrWhiteSpace(tagNo))
            return false;

        var trimmed = tagNo.Trim();

        // Split from the right because branchCode may contain hyphens (e.g. MA-IN-26-000001)
        var lastHyphen = trimmed.LastIndexOf('-');
        if (lastHyphen <= 0)
            return false;

        var secondLastHyphen = trimmed.LastIndexOf('-', lastHyphen - 1);
        if (secondLastHyphen <= 0)
            return false;

        var candidateBranch = trimmed.Substring(0, secondLastHyphen).Trim();
        var candidateYy = trimmed.Substring(secondLastHyphen + 1, lastHyphen - secondLastHyphen - 1).Trim();
        var candidateSeq = trimmed.Substring(lastHyphen + 1).Trim();

        if (string.IsNullOrWhiteSpace(candidateBranch))
            return false;

        if (candidateYy.Length != 2 || !int.TryParse(candidateYy, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yy) || yy < 0)
            return false;

        if (candidateSeq.Length != 6 || !int.TryParse(candidateSeq, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq) || seq < 1 || seq > MaxSequenceNumber)
            return false;

        branchCode = candidateBranch.ToUpperInvariant();
        year = 2000 + yy;
        sequenceNumber = seq;
        return true;
    }
}
