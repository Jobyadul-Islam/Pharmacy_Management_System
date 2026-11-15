using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using PharmacyManagement.Models.Validation;

namespace PharmacyManagement.Models.ViewModels.Cart
{
    public class CheckoutViewModel : IValidatableObject
    {
        // Ignored on submit: the server always uses the logged-in user's customer ID.
        public int CustomerId { get; set; }

        public int? PrescriptionId { get; set; }

        [Required(ErrorMessage = "Payment method is required.")]
        [RegularExpression(ValidationPatterns.PaymentMethod, ErrorMessage = "Please select a valid payment method.")]
        public string PaymentMethod { get; set; } = string.Empty;

        [Required(ErrorMessage = "Delivery method is required.")]
        [RegularExpression(ValidationPatterns.DeliveryMethod, ErrorMessage = "Please select a valid delivery method.")]
        public string DeliveryMethod { get; set; } = string.Empty;

        [StringLength(300, ErrorMessage = "Address cannot exceed 300 characters.")]
        public string? DeliveryAddress { get; set; }

        // Filled by the controller from the cart (display only, never bound from the form).
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public List<CartItemViewModel> CartItems { get; set; } = new();

        public IEnumerable<SelectListItem>? Customers { get; set; }
        public IEnumerable<SelectListItem>? Prescriptions { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (DeliveryMethod == "HomeDelivery")
            {
                if (string.IsNullOrWhiteSpace(DeliveryAddress))
                {
                    yield return new ValidationResult(
                        "Delivery address is required for home delivery.",
                        new[] { nameof(DeliveryAddress) });
                }
                else if (DeliveryAddress.Trim().Length < 10)
                {
                    yield return new ValidationResult(
                        "Please enter a complete delivery address (at least 10 characters).",
                        new[] { nameof(DeliveryAddress) });
                }
            }
            // The cart items themselves are not posted by the form; the controller loads them
            // from the customer's cart and validates them against the database.
        }
    }
}
