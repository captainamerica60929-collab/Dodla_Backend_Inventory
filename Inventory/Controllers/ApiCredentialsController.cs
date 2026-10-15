using Inventory.Application.ApiCredentials.DTOs;
using Inventory.Application.ApiCredentials.Services;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/api-credentials")]
public class ApiCredentialsController : ControllerBase
{
    private readonly IApiCredentialService _credentialService;
    private readonly IHostEnvironment _environment;

    public ApiCredentialsController(IApiCredentialService credentialService, IHostEnvironment environment)
    {
        _credentialService = credentialService;
        _environment = environment;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiCredentialDto>>> GetAll(CancellationToken ct)
    {
        var credentials = await _credentialService.GetAllAsync(ct);
        return Ok(credentials);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiCredentialDto>> GetById(int id, CancellationToken ct)
    {
        var credential = await _credentialService.GetByIdAsync(id, ct);
        return Ok(credential);
    }

    [HttpPost]
    public async Task<ActionResult<ApiCredentialDto>> Create([FromBody] CreateApiCredentialDto dto, CancellationToken ct)
    {
        var credential = await _credentialService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = credential.Id }, credential);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiCredentialDto>> Update(int id, [FromBody] UpdateApiCredentialDto dto, CancellationToken ct)
    {
        var credential = await _credentialService.UpdateAsync(id, dto, ct);
        return Ok(credential);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _credentialService.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:int}/mappings")]
    public async Task<ActionResult<IReadOnlyList<ApiFieldMappingDto>>> GetMappings(int id, CancellationToken ct)
    {
        var mappings = await _credentialService.GetMappingsAsync(id, ct);
        return Ok(mappings);
    }

    [HttpPut("{id:int}/mappings")]
    public async Task<ActionResult<IReadOnlyList<ApiFieldMappingDto>>> ReplaceMappings(
        int id,
        [FromBody] List<UpsertApiFieldMappingDto> mappings,
        CancellationToken ct)
    {
        var updated = await _credentialService.ReplaceMappingsAsync(id, mappings, ct);
        return Ok(updated);
    }

    // TODO: [Authorize(Roles = "Admin")] - Add Admin authorization when JWT auth is implemented.
    // Register the test and preview endpoints only in the Development environment until JWT [Authorize(Admin)] exists.
    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<ApiTestResultDto>> TestConnection(int id, CancellationToken ct)
    {
        if (!_environment.IsDevelopment())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "API test endpoint is restricted to Development environment until JWT Admin authorization is active."
            });
        }

        var result = await _credentialService.TestConnectionAsync(id, ct);
        return Ok(result);
    }

    // TODO: [Authorize(Roles = "Admin")] - Add Admin authorization when JWT auth is implemented.
    // Register the test and preview endpoints only in the Development environment until JWT [Authorize(Admin)] exists.
    [HttpPost("{id:int}/preview-mapping")]
    public async Task<ActionResult<MappingPreviewResultDto>> PreviewMapping(int id, CancellationToken ct)
    {
        if (!_environment.IsDevelopment())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "API preview-mapping endpoint is restricted to Development environment until JWT Admin authorization is active."
            });
        }

        var result = await _credentialService.PreviewMappingAsync(id, ct);
        return Ok(result);
    }

    // TODO: [Authorize(Roles = "Admin")] - Add Admin authorization when JWT auth is implemented.
    // Register the fetch endpoint only in the Development environment until JWT [Authorize(Admin)] exists.
    [HttpPost("{id:int}/fetch")]
    public async Task<ActionResult<Inventory.Application.GrnResponses.DTOs.FetchResultDto>> Fetch(
        int id,
        [FromBody] Inventory.Application.GrnResponses.DTOs.FetchGrnRequestDto request,
        CancellationToken ct)
    {
        if (!_environment.IsDevelopment())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "Live GRN fetch endpoint is restricted to Development environment until JWT Admin authorization is active."
            });
        }

        var result = await _credentialService.FetchAsync(id, request, ct);
        return Ok(result);
    }
}
