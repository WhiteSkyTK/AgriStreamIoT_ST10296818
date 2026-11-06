using System.ComponentModel.DataAnnotations;
using AgriStreamIoT.Shared.Constants;

namespace AgriStreamIoT.Web.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Enter your full name."), StringLength(120)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your email address."), EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(20)]
    [Display(Name = "Mobile number")]
    public string? PhoneNumber { get; set; }

    [Required]
    [Display(Name = "Your role on the farm")]
    public string Role { get; set; } = UserRoles.Farmer;

    [Required, MinLength(8, ErrorMessage = "Use at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class SettingsViewModel
{
    [Required(ErrorMessage = "Enter your full name."), StringLength(120)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Phone, StringLength(20)]
    [Display(Name = "Mobile number")]
    public string? PhoneNumber { get; set; }

    // Read-only on the page.
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(8, ErrorMessage = "Use at least 8 characters."), DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "The new passwords do not match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
