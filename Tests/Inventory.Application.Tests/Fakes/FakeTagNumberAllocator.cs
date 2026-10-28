using System.Collections.Concurrent;
using Inventory.Application.Common.Interfaces;

namespace Inventory.Application.Tests.Fakes;

public class FakeTagNumberAllocator : ITagNumberAllocator
{
    private readonly ConcurrentDictionary<(int BranchId, int Year), int> _counters = new();

    public Task<int> AllocateAsync(int branchId, int year, int count, CancellationToken ct = default)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than zero.");

        var key = (branchId, year);
        var firstNo = _counters.AddOrUpdate(
            key,
            addValueFactory: _ =>
            {
                if (count > 999999)
                    throw new InvalidOperationException("Tag sequence counter exceeded maximum allowed value of 999999.");
                return count;
            },
            updateValueFactory: (_, currentLast) =>
            {
                if (currentLast + count > 999999)
                    throw new InvalidOperationException("Tag sequence counter exceeded maximum allowed value of 999999.");
                return currentLast + count;
            });

        return Task.FromResult(firstNo - count + 1);
    }
}
