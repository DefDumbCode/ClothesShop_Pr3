namespace ClothesShop_Pr3.Contracts;

public record RegisterRequest(string Login, string Password, string Phone, string Address);
public record LoginRequest(string Login, string Password);
public record OrderItemRequest(int ClothesSizeId, int Quantity);
public record CreateOrderRequest(List<OrderItemRequest>? Items);
public record ChangeStatusRequest(int StatusId);
public record ChangeStockRequest(int Quantity);
