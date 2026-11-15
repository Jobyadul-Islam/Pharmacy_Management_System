namespace PharmacyManagement.Models.ViewModels.Cart
{
    public class CartPageViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();

        public decimal GrandTotal => Items.Sum(i => i.TotalPrice);

        public int TotalQuantity => Items.Sum(i => i.Quantity);

        public bool CanCheckout => Items.Any() && Items.All(i => i.Problem == null);

        public bool NeedsPrescription => Items.Any(i => i.RequiresPrescription);
    }
}
