using System.ComponentModel.DataAnnotations;

namespace CropCare.Web.Models;

public sealed class RegisterViewModel
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string FirstName { get; set; } = "";

    [Required, StringLength(80, MinimumLength = 1)]
    public string LastName { get; set; } = "";

    [Required, StringLength(250, MinimumLength = 1)]
    public string Address { get; set; } = "";

    [Required, RegularExpression(@"^[A-Za-z0-9._-]{3,30}$",
        ErrorMessage = "Use 3-30 letters, numbers, dots, dashes, or underscores.")]
    public string Username { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, MinLength(6), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = "";
}
