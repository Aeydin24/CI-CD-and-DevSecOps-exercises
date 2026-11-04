using OrderManagement.Api.Models;

namespace OrderManagement.Tests;

public class OrderTests
{
    [Fact]
    public void NewOrder_HasCreatedStatus()
    {
        var order = new Order("Alice", "Keyboard", 2);

        Assert.Equal(OrderStatus.Created, order.Status);
    }

    [Fact]
    public void QuantityZero_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Order("Alice", "Keyboard", 0));
    }

    [Fact]
    public void CreatedOrder_CanBeCancelled()
    {
        var order = new Order("Alice", "Keyboard", 2);

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CreatedOrder_CanBeDelivered()
    {
        var order = new Order("Alice", "Keyboard", 2);

        order.Deliver();

        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void DeliveredOrder_CannotBeCancelled()
    {
        var order = new Order("Alice", "Keyboard", 2);
        order.Deliver();

        Assert.Throws<InvalidOperationException>(
            () => order.Cancel());
    }
}
