using ClothesShop_Pr3.Models;

namespace ClothesShop_Pr3.DTO
{
    public record ClothesDto(string ClothingName, string Description, int SaleQuantity, string Type, double price, List<ClothesSizeDto> ClothesSizesList, bool IsAvailable);
}