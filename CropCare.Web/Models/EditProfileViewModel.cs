using System.ComponentModel.DataAnnotations;

namespace CropCare.Web.Models;

public sealed class EditProfileViewModel
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string FirstName { get; set; } = "";

    [StringLength(80)]
    public string MiddleName { get; set; } = "";

    [Required, StringLength(80, MinimumLength = 1)]
    public string LastName { get; set; } = "";

    [StringLength(250)]
    public string Address { get; set; } = "";

    [StringLength(30)]
    public string PhoneNumber { get; set; } = "";
}
