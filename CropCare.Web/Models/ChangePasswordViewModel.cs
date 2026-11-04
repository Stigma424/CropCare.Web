using System.ComponentModel.DataAnnotations;

namespace CropCare.Web.Models;

public sealed class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 8), DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
    public string ConfirmNewPassword { get; set; } = "";
}
