using PharmacyManagement.Models;
using PharmacyManagement.Models.ViewModels.Cart;

namespace PharmacyManagement.Services
{
    /// <summary>
    /// Price and stock rules shared by the catalog, cart and checkout. Medicines passed in
    /// must have their Stocks loaded.
    /// </summary>
    public static class MedicinePricing
    {
        // Only unexpired batches can be sold.
        public static int GetSellableStock(Medicine medicine)
        {
            return medicine.Stocks
                .Where(s => s.ExpiryDate.Date > DateTime.Today)
                .Sum(s => s.Quantity);
        }

        // The selling price of the most recently added batch (same rule as the catalog page).
        public static decimal GetCurrentPrice(Medicine medicine)
        {
            return medicine.Stocks
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => s.SellingPrice)
                .FirstOrDefault();
        }

        // Builds display rows for the cart/checkout from the cookie lines and the database.
        public static List<CartItemViewModel> BuildCartItems(IEnumerable<CartService.CartLine> lines, IReadOnlyDictionary<int, Medicine> medicines)
        {
            var items = new List<CartItemViewModel>();

            foreach (var line in lines)
            {
                if (medicines.TryGetValue(line.MedicineId, out var medicine))
                {
                    items.Add(new CartItemViewModel
                    {
                        MedicineId = medicine.MedicineId,
                        MedicineName = medicine.Name,
                        Strength = medicine.Strength,
                        ImageUrl = medicine.ImageUrl,
                        Quantity = line.Quantity,
                        UnitPrice = GetCurrentPrice(medicine),
                        StockLimit = GetSellableStock(medicine),
                        IsActive = medicine.IsActive,
                        RequiresPrescription = medicine.IsPrescriptionRequired
                    });
                }
                else
                {
                    items.Add(new CartItemViewModel
                    {
                        MedicineId = line.MedicineId,
                        MedicineName = "Unavailable medicine",
                        Quantity = line.Quantity,
                        IsActive = false
                    });
                }
            }

            return items;
        }
    }
}
