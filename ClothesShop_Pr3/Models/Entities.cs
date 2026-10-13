namespace ClothesShop_Pr3.Models;

public class Clothes
{
    public int ID { get; set; }
    public string ClothingName { get; set; } = "";
    public string Description { get; set; } = "";
    public int SaleQuantity { get; set; }
    public int TypeID { get; set; }
    public bool IsAvailable { get; set; }
    public decimal Price { get; set; }
}

public class ClothesSize
{
    public int ID { get; set; }
    public int ClothesID { get; set; }
    public int SizeID { get; set; }
    public int Quantity { get; set; }
}

public class ClothingType
{
    public int ID { get; set; }
    public string TypeName { get; set; } = "";
}

public class Size
{
    public int ID { get; set; }
    public string SizeMark { get; set; } = "";
    public int SizeNumber { get; set; }
}

public class ShopUser
{
    public int ID { get; set; }
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Login { get; set; } = "";
    public string HashPass { get; set; } = "";
    public int RoleID { get; set; }
}

public class Role
{
    public int ID { get; set; }
    public string Name { get; set; } = "";
}

public class ShopOrder
{
    public int ID { get; set; }
    public DateTime CreatedAt { get; set; }
    public int StatusID { get; set; }
    public int UserID { get; set; }
}

public class OrderClothes
{
    public int ID { get; set; }
    public int OrderID { get; set; }
    public int ClothesSizeID { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class OrderStatus
{
    public int ID { get; set; }
    public string Name { get; set; } = "";
}
