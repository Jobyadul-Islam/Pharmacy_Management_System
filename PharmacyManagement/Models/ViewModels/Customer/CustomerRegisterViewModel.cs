using System.ComponentModel.DataAnnotations;
using PharmacyManagement.Models.Validation;

namespace PharmacyManagement.Models.ViewModels.Customer
{
    public class CustomerRegisterViewModel
    {
        [Required(ErrorMessage = "Customer name is required.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 150 characters.")]
        [RegularExpression(ValidationPatterns.PersonName, ErrorMessage = ValidationPatterns.PersonNameMessage)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Age is required.")]
        [Range(1, 120, ErrorMessage = "Age must be between 1 and 120.")]
        public int? Age { get; set; }

        [Required(ErrorMessage = "Gender is required.")]
        [RegularExpression(ValidationPatterns.Gender, ErrorMessage = ValidationPatterns.GenderMessage)]
        public string Gender { get; set; } = string.Empty;

        [StringLength(20)]
        [RegularExpression(ValidationPatterns.Phone, ErrorMessage = ValidationPatterns.PhoneMessage)]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Email address is required.")]
        [StringLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
        [RegularExpression(ValidationPatterns.Email, ErrorMessage = ValidationPatterns.EmailMessage)]
        public string Email { get; set; } = string.Empty;

        [StringLength(300, ErrorMessage = "Address cannot exceed 300 characters.")]
        public string? Address { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at most {1} characters long.", MinimumLength = 6)]
        [RegularExpression(ValidationPatterns.Password, ErrorMessage = ValidationPatterns.PasswordMessage)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
