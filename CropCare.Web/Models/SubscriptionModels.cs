using System.ComponentModel.DataAnnotations;

namespace CropCare.Web.Models;

public sealed class SubscriptionStatusDto
{
    public bool IsSubscribed { get; set; }
    public bool? VerifiedMembership => Status?.ToLowerInvariant() switch { "pro" when IsSubscribed => true, "free" or "pending" or "rejected" when !IsSubscribed => false, _ => null };
    public string Status { get; set; } = "unknown";
    public string ReferenceLastFour { get; set; } = "";
    public long? SubmittedAtMs { get; set; }
    public string Message { get; set; } = "";
}

public sealed class SubscriptionPageViewModel
{
    public SubscriptionStatusDto Status { get; set; } = new();
    public string? Error { get; set; }

    [Required(ErrorMessage = "Enter your payment reference number.")]
    [RegularExpression(@"^[0-9]{8,20}$",
        ErrorMessage = "Enter a reference number with 8 to 20 digits.")]
    [Display(Name = "GCash Reference Number")]
    public string ReferenceNumber { get; set; } = "";
}
