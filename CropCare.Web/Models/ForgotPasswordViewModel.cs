using System.ComponentModel.DataAnnotations;

namespace CropCare.Web.Models;

public sealed class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";
}
