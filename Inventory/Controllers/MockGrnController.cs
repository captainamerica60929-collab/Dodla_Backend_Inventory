using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

/// <summary>
/// Development-only Mock GRN API for exercising authentication, mapping, and error paths.
/// Not accessible outside Development environment.
/// </summary>
[ApiController]
[Route("api/dev/mock-grn")]
public class MockGrnController : ControllerBase
{
    private readonly IHostEnvironment _environment;

    public MockGrnController(IHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet]
    [HttpPost]
    public IActionResult GetOrPostMockGrn(
        [FromQuery] string? plant,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] string? shape,
        [FromBody] MockGrnRequestBody? body = null)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound("Mock GRN API is only available in Development environment.");
        }

        // Auth check: require non-empty X-Api-Key
        if (!Request.Headers.TryGetValue("X-Api-Key", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "Missing or empty X-Api-Key header."
            });
        }

        // Support parameters from query string or body
        var effectivePlant = plant ?? body?.Plant;
        var effectiveShape = (shape ?? body?.Shape ?? "nested").ToLowerInvariant();

        var allItems = GetDeterministicRows();

        if (!string.IsNullOrWhiteSpace(effectivePlant))
        {
            allItems = allItems
                .Where(item => item.TryGetValue("Plant", out var p) && string.Equals(p?.ToString(), effectivePlant.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return effectiveShape switch
        {
            "array" => Ok(allItems),
            "object" => Ok(allItems.Count > 0 ? allItems[0] : new Dictionary<string, object>()),
            _ => Ok(new
            {
                status = "OK",
                data = new
                {
                    items = allItems
                }
            })
        };
    }

    private static List<Dictionary<string, object>> GetDeterministicRows()
    {
        return new List<Dictionary<string, object>>
        {
            new() { ["GRNNo"] = "50001001", ["GRN Date"] = "2026-10-01 09:30 AM", ["PartName"] = "Motor Rotor", ["InvoiceNo"] = "INV-1001", ["Description"] = "Rotor Part", ["Qty"] = 50, ["Plant"] = "1000" },
            new() { ["GRNNo"] = "50001002", ["GRN Date"] = "2026-10-02 10:15 AM", ["PartName"] = "Stator Frame", ["InvoiceNo"] = "INV-1002", ["Description"] = "Stator Part", ["Qty"] = 75, ["Plant"] = "1000" },
            new() { ["GRNNo"] = "50001003", ["GRN Date"] = "2026-10-03 11:00 AM", ["PartName"] = "Copper Wire Spool", ["InvoiceNo"] = "INV-1003", ["Description"] = "Wire Spool", ["Qty"] = 120, ["Plant"] = "1000" },
            new() { ["GRNNo"] = "50001004", ["GRN Date"] = "2026-10-04 02:45 PM", ["PartName"] = "Terminal Box", ["InvoiceNo"] = "INV-1004", ["Description"] = "Terminal Box", ["Qty"] = 30, ["Plant"] = "1000" },
            new() { ["GRNNo"] = "50001005", ["GRN Date"] = "2026-10-05 04:20 PM", ["PartName"] = "Ball Bearing 6205", ["InvoiceNo"] = "INV-1005", ["Description"] = "Deep groove ball bearing", ["Qty"] = 200, ["Plant"] = "1000" },
            new() { ["GRNNo"] = "50002001", ["GRN Date"] = "2026-10-06 08:30 AM", ["PartName"] = "Cast Iron Flange", ["InvoiceNo"] = "INV-2001", ["Description"] = "Mounting Flange", ["Qty"] = 40, ["Plant"] = "2000" },
            new() { ["GRNNo"] = "50002002", ["GRN Date"] = "2026-10-07 01:10 PM", ["PartName"] = "Cooling Fan Blade", ["InvoiceNo"] = "INV-2002", ["Description"] = "Plastic cooling fan", ["Qty"] = 150, ["Plant"] = "2000" },
            new() { ["GRNNo"] = "50002003", ["GRN Date"] = "2026-10-08 03:50 PM", ["PartName"] = "Drive Shaft", ["InvoiceNo"] = "INV-2003", ["Description"] = "Steel shaft 28mm", ["Qty"] = 60, ["Plant"] = "2000" },
            new() { ["GRNNo"] = "50002004", ["GRN Date"] = "2026-10-09 11:30 AM", ["PartName"] = "Gasket Seal", ["InvoiceNo"] = "INV-2004", ["Description"] = "Rubber seal ring", ["Qty"] = 500, ["Plant"] = "2000" },
            new() { ["GRNNo"] = "50002005", ["GRN Date"] = "2026-10-10 09:00 AM", ["PartName"] = "Nameplate Decal", ["InvoiceNo"] = "INV-2005", ["Description"] = "Rating plate sticker", ["Qty"] = 300, ["Plant"] = "2000" },
            // Row 11: Invalid Qty
            new() { ["GRNNo"] = "50009991", ["GRN Date"] = "2026-10-11 12:00 PM", ["PartName"] = "Defective Qty Item", ["InvoiceNo"] = "INV-9991", ["Description"] = "Invalid qty sample", ["Qty"] = "abc", ["Plant"] = "1000" },
            // Row 12: Missing GRNNo
            new() { ["GRN Date"] = "2026-10-12 02:00 PM", ["PartName"] = "Missing GrnNo Item", ["InvoiceNo"] = "INV-9992", ["Description"] = "Missing GrnNo sample", ["Qty"] = 10, ["Plant"] = "2000" }
        };
    }
}

public class MockGrnRequestBody
{
    public string? Plant { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public string? Shape { get; set; }
}
