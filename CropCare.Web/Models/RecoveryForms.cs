using System.ComponentModel.DataAnnotations;

namespace CropCare.Web.Models;

public sealed class RequestRecoveryCodeViewModel
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";
}

public sealed class VerifyRecoveryCodeViewModel
{
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the six-digit code.")]
    public string Code { get; set; } = "";
}

public sealed class NewRecoveryPasswordViewModel
{
    [Required, StringLength(128, MinimumLength = 8), DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword),
        ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class RecoveryVerificationResponse
{
    public string ResetTicket { get; set; } = "";
}