namespace ClothesShop_Pr3.DTO
{
    public record ClotheDto(string ClothingName, string Description, int SaleQuantity, string Type, double price, ClothesSizeDto ClotheSize);
}