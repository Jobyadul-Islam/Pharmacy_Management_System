using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using PharmacyManagement.Models.Validation;

namespace PharmacyManagement.Models.ViewModels.Stock
{
    public class StockCreateEditViewModel : IValidatableObject
    {
        public int StockId { get; set; }

        [Required(ErrorMessage = "Medicine is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a medicine.")]
        [Display(Name = "Medicine")]
        public int MedicineId { get; set; }

        [Required(ErrorMessage = "Batch number is required.")]
        [StringLength(50, ErrorMessage = "Batch number cannot exceed 50 characters.")]
        [RegularExpression(ValidationPatterns.BatchNumber, ErrorMessage = ValidationPatterns.BatchNumberMessage)]
        [Display(Name = "Batch Number")]
        public string BatchNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Quantity is required.")]
        [Range(0, 1_000_000, ErrorMessage = "Quantity must be between 0 and 1,000,000.")]
        public int? Quantity { get; set; }

        [Required(ErrorMessage = "Purchase price is required.")]
        [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Purchase price must be between 0.01 and 1,000,000.")]
        [Display(Name = "Purchase Price (BDT)")]
        public decimal? PurchasePrice { get; set; }

        [Required(ErrorMessage = "Selling price is required.")]
        [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Selling price must be between 0.01 and 1,000,000.")]
        [Display(Name = "Selling Price (BDT)")]
        public decimal? SellingPrice { get; set; }

        [Required(ErrorMessage = "Manufacturing date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Manufacturing Date")]
        public DateTime? ManufacturingDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Expiry date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Expiry Date")]
        public DateTime? ExpiryDate { get; set; } = DateTime.Today.AddMonths(12);

        public IEnumerable<SelectListItem>? Medicines { get; set; }

        // Cross-field rules that single-property attributes can't express.
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (PurchasePrice.HasValue && SellingPrice.HasValue && SellingPrice.Value < PurchasePrice.Value)
            {
                yield return new ValidationResult(
                    "Selling price cannot be lower than the purchase price.",
                    new[] { nameof(SellingPrice) });
            }

            if (ManufacturingDate.HasValue && ManufacturingDate.Value.Date > DateTime.Today)
            {
                yield return new ValidationResult(
                    "Manufacturing date cannot be in the future.",
                    new[] { nameof(ManufacturingDate) });
            }

            if (ManufacturingDate.HasValue && ExpiryDate.HasValue && ExpiryDate.Value.Date <= ManufacturingDate.Value.Date)
            {
                yield return new ValidationResult(
                    "Expiry date must be after the manufacturing date.",
                    new[] { nameof(ExpiryDate) });
            }

            // New batches must not already be expired. (Existing batches may be edited after they expire,
            // e.g. to set their quantity to 0, so this rule only applies when adding stock.)
            if (StockId == 0 && ExpiryDate.HasValue && ExpiryDate.Value.Date <= DateTime.Today)
            {
                yield return new ValidationResult(
                    "You cannot add stock that has already expired.",
                    new[] { nameof(ExpiryDate) });
            }
        }
    }
}
