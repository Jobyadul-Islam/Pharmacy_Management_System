using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Models;
using PharmacyManagement.Models.ViewModels.Cart;
using PharmacyManagement.Services;

namespace PharmacyManagement.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CartController : Controller
    {
        private readonly AppDbContext _context;
        private readonly CartService _cart;

        public CartController(AppDbContext context, CartService cart)
        {
            _context = context;
            _cart = cart;
        }

        // VIEW CART
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var lines = _cart.GetItems();
            var medicines = await LoadMedicinesAsync(lines.Select(l => l.MedicineId));

            var viewModel = new CartPageViewModel
            {
                Items = MedicinePricing.BuildCartItems(lines, medicines)
            };

            return View(viewModel);
        }

        // ADD TO CART (from the catalog)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int medicineId, int quantity = 1, string? returnUrl = null)
        {
            quantity = Math.Clamp(quantity, 1, CartService.MaxQuantityPerItem);

            var medicine = (await LoadMedicinesAsync(new[] { medicineId })).GetValueOrDefault(medicineId);

            if (medicine == null || !medicine.IsActive)
            {
                TempData["CartError"] = "That medicine is not available.";
                return RedirectBack(returnUrl);
            }

            int stock = MedicinePricing.GetSellableStock(medicine);
            int alreadyInCart = _cart.GetQuantity(medicineId);
            int newQuantity = alreadyInCart + quantity;

            if (stock <= 0)
            {
                TempData["CartError"] = $"{medicine.Name} is out of stock.";
            }
            else if (newQuantity > Math.Min(stock, CartService.MaxQuantityPerItem))
            {
                int max = Math.Min(stock, CartService.MaxQuantityPerItem);
                TempData["CartError"] = $"You can't add more {medicine.Name}. You already have {alreadyInCart} in your cart (maximum {max}).";
            }
            else if (alreadyInCart == 0 && _cart.GetItems().Count >= CartService.MaxDistinctItems)
            {
                TempData["CartError"] = $"Your cart can hold at most {CartService.MaxDistinctItems} different medicines.";
            }
            else
            {
                _cart.SetQuantity(medicineId, newQuantity);
                TempData["CartMessage"] = $"{medicine.Name} added to your cart.";
            }

            return RedirectBack(returnUrl);
        }

        // UPDATE QUANTITIES (quantities[medicineId] = new quantity)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(Dictionary<int, int>? quantities)
        {
            if (quantities == null || quantities.Count == 0)
            {
                return RedirectToAction(nameof(Index));
            }

            var inCart = _cart.GetItems().Select(i => i.MedicineId).ToHashSet();
            var medicines = await LoadMedicinesAsync(quantities.Keys);
            var notes = new List<string>();

            foreach (var (medicineId, requested) in quantities)
            {
                if (!inCart.Contains(medicineId)) continue;   // only change items that are really in the cart

                int quantity = Math.Clamp(requested, 0, CartService.MaxQuantityPerItem);

                if (medicines.TryGetValue(medicineId, out var medicine))
                {
                    int stock = MedicinePricing.GetSellableStock(medicine);
                    if (quantity > stock && stock > 0)
                    {
                        notes.Add($"Only {stock} {medicine.Name} in stock, so the quantity was set to {stock}.");
                        quantity = stock;
                    }
                }

                _cart.SetQuantity(medicineId, quantity);   // 0 removes the item
            }

            TempData[notes.Any() ? "CartError" : "CartMessage"] = notes.Any() ? string.Join(" ", notes) : "Cart updated.";
            return RedirectToAction(nameof(Index));
        }

        // REMOVE ONE ITEM
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Remove(int medicineId)
        {
            _cart.Remove(medicineId);
            TempData["CartMessage"] = "Item removed from your cart.";
            return RedirectToAction(nameof(Index));
        }

        // EMPTY THE CART
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Clear()
        {
            _cart.Clear();
            TempData["CartMessage"] = "Your cart is now empty.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<Dictionary<int, Medicine>> LoadMedicinesAsync(IEnumerable<int> medicineIds)
        {
            var ids = medicineIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, Medicine>();

            return await _context.Medicines
                .AsNoTracking()
                .Include(m => m.Stocks)
                .Where(m => ids.Contains(m.MedicineId))
                .ToDictionaryAsync(m => m.MedicineId);
        }

        private IActionResult RedirectBack(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Home");
        }
    }
}
