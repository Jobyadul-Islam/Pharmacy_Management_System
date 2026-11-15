using System.ComponentModel.DataAnnotations;

namespace PharmacyManagement.Models.ViewModels.Cart
{
    public class CartItemViewModel
    {
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Invalid medicine.")]
        public int MedicineId { get; set; }

        // Everything below is filled from the database for display; it is never read back from a form.
        public string MedicineName { get; set; } = string.Empty;
        public string? Strength { get; set; }
        public string? ImageUrl { get; set; }

        [Required(ErrorMessage = "Quantity is required.")]
        [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100.")]
        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public int StockLimit { get; set; }

        public bool IsActive { get; set; } = true;

        public bool RequiresPrescription { get; set; }

        public decimal TotalPrice
        {
            get { return Quantity * UnitPrice; }
        }

        // Problem with this line that blocks checkout, or null if it can be ordered.
        public string? Problem
        {
            get
            {
                if (!IsActive) return "No longer available - please remove it.";
                if (StockLimit <= 0) return "Out of stock - please remove it.";
                if (Quantity > StockLimit) return $"Only {StockLimit} in stock - please lower the quantity.";
                if (UnitPrice <= 0) return "No price set yet - please remove it.";
                return null;
            }
        }
    }
}
