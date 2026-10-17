using System.Text.Json.Nodes;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Common.Model.Data;
using Middt.Framework.Workflow.Runtime;

namespace Middt.Sample.Api.Controllers;

/// <summary>
/// Exposes workflow instances' JSON <c>Data</c> over the standard <c>api/v1/{entity}/getall|getitems|getbyid</c> contract so
/// Server-Driven UI layouts (<c>data-grid</c>, <c>kpi-row</c>, <c>crud-page</c>) and <c>x-lookup</c> selects operate directly on
/// <c>WorkflowDbContext</c> JSON documents without separate relational domain tables.
/// </summary>
[ApiController]
[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/{entity:regex(^(?i)(customeronboarding|customer-onboarding|creditallocation|credit-allocation|orderpaymentorchestration|order-payment-orchestration|slaescalation|sla-escalation|risklimiteval|risk-limit-eval|workflowinstance)$)}")]
public sealed class WorkflowDataController(WorkflowInstanceDataQuery query) : ControllerBase
{
    [HttpGet("getall")]
    public Task<BaseResponseDataModel<List<JsonObject>>> GetAllAsync(string entity, CancellationToken cancellationToken) =>
        query.GetAllAsync(entity, User, cancellationToken);

    [HttpPost("getitems")]
    public Task<BaseResponseDataModel<List<JsonObject>>> GetItemsAsync(
        string entity,
        [FromBody] JsonObject? searchRequest,
        CancellationToken cancellationToken) =>
        query.GetItemsAsync(entity, searchRequest, User, cancellationToken);

    [HttpGet("getbyid")]
    public Task<BaseResponseDataModel<JsonObject>> GetByIdAsync(
        string entity,
        [FromQuery] string id,
        CancellationToken cancellationToken) =>
        query.GetByIdAsync(entity, id, User, cancellationToken);

    /// <summary>Local gateway simulation endpoint used by <c>http</c> workflow tasks.</summary>
    [AllowAnonymous]
    [HttpGet("ping")]
    [HttpPost("ping")]
    public IActionResult Ping(string entity, [FromBody] JsonObject? payload = null) =>
        Ok(new
        {
            status = "Approved",
            gateway = entity,
            verified = true,
            referenceCode = $"GW-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            processedAt = DateTimeOffset.UtcNow,
            echo = payload,
        });
}
