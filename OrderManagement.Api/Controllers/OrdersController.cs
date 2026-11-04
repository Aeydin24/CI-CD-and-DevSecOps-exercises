using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OrderManagement.Api.Data;
using OrderManagement.Api.Models;
using OrderManagement.Api.Services;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(
    OrderService orders,
    OrderDbContext db,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request)
    {
        try
        {
            var order = await orders.CreateAsync(request);
            return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var order = await orders.GetAsync(id);
        if (order is null) return NotFound();

        var json = JsonConvert.SerializeObject(order);
        return Content(json, "application/json");
    }

    [HttpPatch("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        try
        {
            var order = await orders.CancelAsync(id);
            return order is null ? NotFound() : Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpPatch("{id:guid}/deliver")]
    public async Task<IActionResult> Deliver(Guid id)
    {
        try
        {
            var order = await orders.DeliverAsync(id);
            return order is null ? NotFound() : Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string customerName)
    {
        try
        {
            var sql =
                $"SELECT * FROM Orders WHERE CustomerName = '{customerName}'";

            var results = await db.Orders
                .FromSqlRaw(sql)
                .ToListAsync();

            return Ok(results);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.ToString());
        }
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetDocument(
        [FromQuery] string fileName)
    {
        try
        {
            var path = Path.Combine(
                environment.ContentRootPath,
                "Documents",
                fileName);

            var content = await System.IO.File.ReadAllTextAsync(path);

            return Content(content, "text/plain");
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.ToString());
        }
    }
}
