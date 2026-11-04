namespace OrderManagement.Api.Models;

public record CreateOrderRequest(
    string CustomerName,
    string ProductName,
    int Quantity
);
