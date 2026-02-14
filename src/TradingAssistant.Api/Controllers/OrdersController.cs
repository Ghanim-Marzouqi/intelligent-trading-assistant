using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TradingAssistant.Api.Services.Orders;

namespace TradingAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderManager _orderManager;
    private readonly IApprovalTokenStore _approvalStore;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderManager orderManager,
        IApprovalTokenStore approvalStore,
        ILogger<OrdersController> logger)
    {
        _orderManager = orderManager;
        _approvalStore = approvalStore;
        _logger = logger;
    }

    [HttpGet("pending")]
    public ActionResult<IEnumerable<PreparedOrder>> GetPendingOrders()
    {
        return Ok(_approvalStore.GetPending());
    }

    [HttpPost("{token}/approve")]
    [EnableRateLimiting("trading")]
    public async Task<IActionResult> ApproveOrder(string token)
    {
        try
        {
            var success = await _orderManager.ApproveOrderAsync(token);
            if (!success)
                return BadRequest(new { error = "Order not found, expired, or execution failed" });

            return Ok(new { message = "Order approved and executed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving order {Token}", token);
            return StatusCode(500, new { error = "Failed to execute order" });
        }
    }

    [HttpPost("{token}/reject")]
    public async Task<IActionResult> RejectOrder(string token)
    {
        await _orderManager.RejectOrderAsync(token);
        return Ok(new { message = "Order rejected" });
    }
}
