using Inventory.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/meta")]
public class MetadataController : ControllerBase
{
    [HttpGet("target-columns")]
    public ActionResult<IReadOnlyList<string>> GetTargetColumns()
    {
        var columns = Enum.GetNames<TargetColumn>();
        return Ok(columns);
    }
}
