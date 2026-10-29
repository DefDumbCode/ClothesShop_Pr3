using System.Drawing;

namespace ClothesShop_Pr3
{
    public record RegistrationRequest(string Login, string Password, string PasswordRep,
        string Phone, string Address);
}
