
namespace CropCare.Web.Models;

public sealed class CropCareProfile
{
    public string UserId { get; set; } = "";

    public string? FirstName { get; set; }

    public string? MiddleName { get; set; }

    public string? LastName { get; set; }

    public string? Username { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? PhoneNumber { get; set; }
}
