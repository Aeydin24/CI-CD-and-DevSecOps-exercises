using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Data;
using OrderManagement.Api.Models;

namespace OrderManagement.Api.Services;

public class OrderService(OrderDbContext db)
{
    public async Task<Order> CreateAsync(CreateOrderRequest request)
    {
        var order = new Order(
            request.CustomerName,
            request.ProductName,
            request.Quantity);

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    public Task<Order?> GetAsync(Guid id)
    {
        return db.Orders.FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Order?> CancelAsync(Guid id)
    {
        var order = await GetAsync(id);
        if (order is null) return null;

        order.Cancel();
        await db.SaveChangesAsync();
        return order;
    }

    public async Task<Order?> DeliverAsync(Guid id)
    {
        var order = await GetAsync(id);
        if (order is null) return null;

        order.Deliver();
        await db.SaveChangesAsync();
        return order;
    }
}
