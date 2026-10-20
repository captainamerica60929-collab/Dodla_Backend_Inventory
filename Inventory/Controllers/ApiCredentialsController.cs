using Inventory.Application.ApiCredentials.DTOs;
using Inventory.Application.ApiCredentials.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/api-credentials")]
public class ApiCredentialsController : ControllerBase
{
    private readonly IApiCredentialService _credentialService;

    public ApiCredentialsController(IApiCredentialService credentialService)
    {
        _credentialService = credentialService;
    }

    [HttpGet]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<IReadOnlyList<ApiCredentialDto>>> GetAll(CancellationToken ct)
    {
        var credentials = await _credentialService.GetAllAsync(ct);
        return Ok(credentials);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<ApiCredentialDto>> GetById(int id, CancellationToken ct)
    {
        var credential = await _credentialService.GetByIdAsync(id, ct);
        return Ok(credential);
    }

    [HttpPost]
    [Authorize(Policy = "ApiAdmin")]
    public async Task<ActionResult<ApiCredentialDto>> Create([FromBody] CreateApiCredentialDto dto, CancellationToken ct)
    {
        var credential = await _credentialService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = credential.Id }, credential);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "ApiAdmin")]
    public async Task<ActionResult<ApiCredentialDto>> Update(int id, [FromBody] UpdateApiCredentialDto dto, CancellationToken ct)
    {
        var credential = await _credentialService.UpdateAsync(id, dto, ct);
        return Ok(credential);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "ApiAdmin")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _credentialService.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:int}/mappings")]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<IReadOnlyList<ApiFieldMappingDto>>> GetMappings(int id, CancellationToken ct)
    {
        var mappings = await _credentialService.GetMappingsAsync(id, ct);
        return Ok(mappings);
    }

    [HttpPut("{id:int}/mappings")]
    [Authorize(Policy = "ApiAdmin")]
    public async Task<ActionResult<IReadOnlyList<ApiFieldMappingDto>>> ReplaceMappings(
        int id,
        [FromBody] List<UpsertApiFieldMappingDto> mappings,
        CancellationToken ct)
    {
        var updated = await _credentialService.ReplaceMappingsAsync(id, mappings, ct);
        return Ok(updated);
    }

    /// <summary>
    /// Test API connection. Restricted to Admin (ApiAdmin policy).
    /// </summary>
    [HttpPost("{id:int}/test")]
    [Authorize(Policy = "ApiAdmin")]
    public async Task<ActionResult<ApiTestResultDto>> TestConnection(int id, CancellationToken ct)
    {
        var result = await _credentialService.TestConnectionAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Preview field mapping against live data. Restricted to Admin (ApiAdmin policy).
    /// </summary>
    [HttpPost("{id:int}/preview-mapping")]
    [Authorize(Policy = "ApiAdmin")]
    public async Task<ActionResult<MappingPreviewResultDto>> PreviewMapping(int id, CancellationToken ct)
    {
        var result = await _credentialService.PreviewMappingAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Fetch GRN data from the live API. Restricted to Admin (ApiAdmin policy).
    /// </summary>
    [HttpPost("{id:int}/fetch")]
    [Authorize(Policy = "ApiAdmin")]
    public async Task<ActionResult<Inventory.Application.GrnResponses.DTOs.FetchResultDto>> Fetch(
        int id,
        [FromBody] Inventory.Application.GrnResponses.DTOs.FetchGrnRequestDto request,
        CancellationToken ct)
    {
        var result = await _credentialService.FetchAsync(id, request, ct);
        return Ok(result);
    }
}
