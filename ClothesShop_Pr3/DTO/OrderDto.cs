namespace ClothesShop_Pr3.DTO
{
    public record OrderDto(string UserName, string StatusName, List<ClotheDto> ClotheDtoList, DateTime CreatedAt);
}
