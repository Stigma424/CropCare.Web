using System.ComponentModel.DataAnnotations;
namespace CropCare.Web.Models;
public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Username is required."), StringLength(30)]
    [RegularExpression(@"^[A-Za-z0-9._-]{3,30}$", ErrorMessage = "Enter your CropCare username.")]
    private string username = string.Empty;
    public string Username { get => username; set => username = value?.Trim() ?? string.Empty; }
    [Required, StringLength(4096), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
