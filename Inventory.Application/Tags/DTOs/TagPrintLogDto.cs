namespace Inventory.Application.Tags.DTOs;

public record TagPrintLogDto(
    int Id,
    int TagId,
    string Kind,
    string? Reason,
    string PrintedAt,
    int UserId,
    string UserNameSnapshot,
    string? ClientIp);
