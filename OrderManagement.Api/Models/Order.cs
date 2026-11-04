namespace OrderManagement.Api.Models;

public class Order
{
    public Guid Id { get; private set; }
    public string CustomerName { get; private set; } = "";
    public string ProductName { get; private set; } = "";
    public int Quantity { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Order() { } // Required by EF Core

    public Order(string customerName, string productName, int quantity)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name is required.");

        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentException("Product name is required.");

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        Id = Guid.NewGuid();
        CustomerName = customerName;
        ProductName = productName;
        Quantity = quantity;
        Status = OrderStatus.Created;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        if (Status != OrderStatus.Created)
            throw new InvalidOperationException(
                "Only created orders can be cancelled.");

        Status = OrderStatus.Cancelled;
    }

    public void Deliver()
    {
        if (Status != OrderStatus.Created)
            throw new InvalidOperationException(
                "Only created orders can be delivered.");

        Status = OrderStatus.Delivered;
    }
}
