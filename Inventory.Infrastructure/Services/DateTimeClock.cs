using Inventory.Application.Common.Interfaces;

namespace Inventory.Infrastructure.Services;

public class DateTimeClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
