using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Models;
using PharmacyManagement.Models.ViewModels.Cart;
using PharmacyManagement.Models.ViewModels.Prescription;
using PharmacyManagement.Models.ViewModels.Purchase;
using PharmacyManagement.Services;

namespace PharmacyManagement.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CustomerController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly GroqPrescriptionService _groqService;
        private readonly CartService _cart;

        public CustomerController(
            AppDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment webHostEnvironment,
            GroqPrescriptionService groqService,
            CartService cart)
        {
            _context = context;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
            _groqService = groqService;
            _cart = cart;
        }

        // MY PURCHASES HISTORY

        [HttpGet]
        public async Task<IActionResult> MyPurchases()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || user.CustomerId == null)
            {
                return Unauthorized();
            }

            var purchases = await _context.Purchases
                .Where(p => p.CustomerId == user.CustomerId.Value)
                .OrderByDescending(p => p.PurchaseDate)
                .Select(p => new PurchaseViewModel
                {
                    PurchaseId = p.PurchaseId,
                    CustomerId = p.CustomerId,
                    TotalAmount = p.TotalAmount,
                    PaymentMethod = p.PaymentMethod,
                    DeliveryMethod = p.DeliveryMethod,
                    DeliveryAddress = p.DeliveryAddress,
                    PrescriptionId = p.PrescriptionId,
                    Status = p.Status,
                    PurchaseDate = p.PurchaseDate,
                    PurchaseItems = p.PurchaseItems.Select(pi => new PurchaseItemViewModel
                    {
                        PurchaseItemId = pi.PurchaseItemId,
                        MedicineId = pi.MedicineId,
                        MedicineName = pi.Medicine != null ? pi.Medicine.Name : "Medicine",
                        Quantity = pi.Quantity,
                        UnitPrice = pi.UnitPrice,
                        // Older orders were saved with TotalPrice = 0, so calculate it.
                        TotalPrice = pi.UnitPrice * pi.Quantity
                    }).ToList()
                })
                .ToListAsync();

            return View("~/Views/Customer/MyPurchases.cshtml", purchases);
        }

        // MY PRESCRIPTIONS

        [HttpGet]
        public async Task<IActionResult> MyPrescriptions()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || user.CustomerId == null)
            {
                return Unauthorized();
            }

            var presList = await _context.Prescriptions
                .Where(p => p.CustomerId == user.CustomerId.Value)
                .OrderByDescending(p => p.UploadedAt)
                .ToListAsync();

            var allMedicines = await _context.Medicines
                .Include(m => m.Stocks)
                .ToListAsync();

            var prescriptions = new List<PrescriptionViewModel>();

            foreach (var p in presList)
            {
                var matchedItems = new List<PrescriptionItemMatchViewModel>();

                if (!string.IsNullOrEmpty(p.ExtractedMedicines))
                {
                    foreach (var med in allMedicines)
                    {
                        if (!string.IsNullOrEmpty(med.Name) &&
                            p.ExtractedMedicines.Contains(med.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            int totalStock = med.Stocks?.Sum(s => s.Quantity) ?? 0;
                            decimal latestPrice = med.Stocks?
                                .OrderByDescending(s => s.CreatedAt)
                                .FirstOrDefault()?.SellingPrice ?? 0.00m;

                            matchedItems.Add(new PrescriptionItemMatchViewModel
                            {
                                MedicineName = med.Name,
                                Dosage = "As directed",
                                MedicineId = med.MedicineId,
                                IsAvailable = totalStock > 0,
                                StockQuantity = totalStock,
                                SellingPrice = latestPrice
                            });
                        }
                    }
                }

                // Determine stock availability text based on AI detection & inventory match
                string inventoryStatus = p.Status ?? "Pending";
                if (!string.IsNullOrEmpty(p.ExtractedMedicines))
                {
                    if (matchedItems.Any())
                    {
                        bool allAvailable = matchedItems.All(m => m.IsAvailable);
                        bool anyAvailable = matchedItems.Any(m => m.IsAvailable);

                        if (allAvailable)
                        {
                            inventoryStatus = "All Available";
                        }
                        else if (anyAvailable)
                        {
                            inventoryStatus = "Partially Available";
                        }
                        else
                        {
                            inventoryStatus = "Out of Stock";
                        }
                    }
                    else
                    {
                        inventoryStatus = "No Catalog Match";
                    }
                }

                prescriptions.Add(new PrescriptionViewModel
                {
                    PrescriptionId = p.PrescriptionId,
                    CustomerId = p.CustomerId,
                    FileUrl = p.FileUrl ?? string.Empty,
                    Notes = p.Notes,
                    ExtractedMedicines = p.ExtractedMedicines,
                    Status = inventoryStatus,
                    UploadedAt = p.UploadedAt,
                    MatchedItems = matchedItems
                });
            }

            return View("~/Views/Customer/MyPrescriptions.cshtml", prescriptions);
        }

        // UPLOAD PRESCRIPTION

        [HttpGet]
        public IActionResult UploadPrescription()
        {
            return View("~/Views/Customer/UploadPrescription.cshtml", new PrescriptionUploadViewModel());
        }

        // UPLOAD PRESCRIPTION (POST & AI SCANNING)

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPrescription(PrescriptionUploadViewModel viewModel)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || user.CustomerId == null)
            {
                return Unauthorized();
            }

            if (ModelState.IsValid)
            {
                if (viewModel.PrescriptionFile == null || viewModel.PrescriptionFile.Length == 0)
                {
                    ModelState.AddModelError("PrescriptionFile", "Please select a valid prescription file or image.");
                    return View("~/Views/Customer/UploadPrescription.cshtml", viewModel);
                }

                try
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "prescriptions");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    // Never use the user's file name on disk; keep only the (already validated) extension.
                    string extension = Path.GetExtension(viewModel.PrescriptionFile.FileName).ToLowerInvariant();
                    string uniqueFileName = Guid.NewGuid().ToString() + extension;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await viewModel.PrescriptionFile.CopyToAsync(fileStream);
                    }

                    string fileUrl = "/uploads/prescriptions/" + uniqueFileName;

                    // Call Groq AI Service to extract prescription info
                    string extractedText = await _groqService.ExtractMedicineInfoAsync(filePath);

                    var prescription = new Prescription
                    {
                        CustomerId = user.CustomerId.Value,
                        FileUrl = fileUrl,
                        Notes = viewModel.Notes?.Trim(),
                        ExtractedMedicines = extractedText,
                        Status = "Pending",
                        UploadedAt = DateTime.Now
                    };

                    _context.Prescriptions.Add(prescription);
                    await _context.SaveChangesAsync();

                    return RedirectToAction(nameof(MyPrescriptions));
                }
                catch (Exception)
                {
                    ModelState.AddModelError("", "An error occurred during upload. Please try again.");
                }
            }

            return View("~/Views/Customer/UploadPrescription.cshtml", viewModel);
        }

        // CHECKOUT

        [HttpGet]
        public async Task<IActionResult> Checkout(int? medicineId, int? quantity)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || user.CustomerId == null)
            {
                return Unauthorized();
            }

            int customerId = user.CustomerId.Value;
            var customer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == customerId);

            // Old "Buy Now" links (Checkout?medicineId=5) still work: they put the item in the cart first.
            if (medicineId.HasValue && medicineId.Value > 0 && _cart.GetQuantity(medicineId.Value) == 0)
            {
                _cart.SetQuantity(medicineId.Value, Math.Clamp(quantity ?? 1, 1, CartService.MaxQuantityPerItem));
                return RedirectToAction(nameof(Checkout));
            }

            var lines = _cart.GetItems();
            if (!lines.Any())
            {
                TempData["CartError"] = "Your cart is empty. Add some medicines first.";
                return RedirectToAction("Index", "Cart");
            }

            var viewModel = new CheckoutViewModel
            {
                CustomerId = customerId,
                PaymentMethod = "CashOnDelivery",
                DeliveryMethod = "HomeDelivery",
                DeliveryAddress = customer?.Address
            };

            var medicines = await LoadCartMedicinesAsync(lines.Select(l => l.MedicineId));
            viewModel.CartItems = MedicinePricing.BuildCartItems(lines, medicines);

            // Show problems (out of stock, prescription needed, ...) up front instead of after "Place Order".
            await ValidateCartAsync(viewModel, customerId, medicines);

            await PopulatePrescriptionsAsync(viewModel, customerId);
            return View("~/Views/Customer/Checkout.cshtml", viewModel);
        }

        // PROCESS CHECKOUT

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessCheckout(CheckoutViewModel viewModel)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || user.CustomerId == null)
            {
                return Unauthorized();
            }

            int customerId = user.CustomerId.Value;
            viewModel.CustomerId = customerId;            // never trust the posted customer ID

            // The items always come from the customer's cart, never from the submitted form.
            var lines = _cart.GetItems();
            if (!lines.Any())
            {
                TempData["CartError"] = "Your cart is empty. Add some medicines first.";
                return RedirectToAction("Index", "Cart");
            }

            // Serializable: stock read for validation can't change before we deduct it (prevents overselling
            // when two customers buy the last units at the same time).
            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var medicines = await LoadCartMedicinesAsync(lines.Select(l => l.MedicineId));
            viewModel.CartItems = MedicinePricing.BuildCartItems(lines, medicines);

            var prescription = await ValidateCartAsync(viewModel, customerId, medicines);

            if (ModelState.IsValid)
            {
                try
                {
                    bool isHomeDelivery = viewModel.DeliveryMethod == "HomeDelivery";

                    var purchase = new Purchase
                    {
                        CustomerId = customerId,
                        PrescriptionId = prescription?.PrescriptionId,
                        PaymentMethod = viewModel.PaymentMethod,
                        DeliveryMethod = viewModel.DeliveryMethod,
                        DeliveryAddress = isHomeDelivery ? viewModel.DeliveryAddress?.Trim() : null,
                        Status = "Pending",
                        PurchaseDate = DateTime.Now
                    };

                    foreach (var group in viewModel.CartItems.GroupBy(i => i.MedicineId))
                    {
                        var medicine = medicines[group.Key];
                        int quantity = group.Sum(i => i.Quantity);
                        decimal unitPrice = MedicinePricing.GetCurrentPrice(medicine);

                        purchase.PurchaseItems.Add(new PurchaseItem
                        {
                            MedicineId = medicine.MedicineId,
                            Quantity = quantity,
                            UnitPrice = unitPrice,
                            TotalPrice = unitPrice * quantity
                        });

                        // Deduct from unexpired batches, soonest expiry first.
                        int remaining = quantity;
                        var batches = medicine.Stocks
                            .Where(s => s.Quantity > 0 && s.ExpiryDate.Date > DateTime.Today)
                            .OrderBy(s => s.ExpiryDate)
                            .ThenBy(s => s.CreatedAt);

                        foreach (var batch in batches)
                        {
                            if (remaining <= 0) break;

                            int take = Math.Min(batch.Quantity, remaining);
                            batch.Quantity -= take;
                            remaining -= take;
                        }

                        if (remaining > 0)
                        {
                            throw new InvalidOperationException("Not enough stock.");
                        }
                    }

                    purchase.TotalAmount = purchase.PurchaseItems.Sum(pi => pi.TotalPrice);

                    _context.Purchases.Add(purchase);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _cart.Clear();
                    TempData["PurchaseMessage"] = $"Thank you! Your order #{purchase.PurchaseId} has been placed. Total: BDT {purchase.TotalAmount:0.00}";
                    return RedirectToAction(nameof(MyPurchases));
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    _context.ChangeTracker.Clear();
                    ModelState.AddModelError("", "An error occurred while processing your order. Please try again.");
                }
            }

            await PopulatePrescriptionsAsync(viewModel, customerId);
            return View("~/Views/Customer/Checkout.cshtml", viewModel);
        }

        #region Checkout Helpers

        // Loaded with tracking so the stock deduction at checkout is saved.
        private async Task<Dictionary<int, Medicine>> LoadCartMedicinesAsync(IEnumerable<int> medicineIds)
        {
            var ids = medicineIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, Medicine>();
            }

            return await _context.Medicines
                .Include(m => m.Stocks)
                .Where(m => ids.Contains(m.MedicineId))
                .ToDictionaryAsync(m => m.MedicineId);
        }

        // Business-rule validation that needs the database. Adds errors to ModelState and
        // returns the attached prescription if it belongs to this customer.
        private async Task<Prescription?> ValidateCartAsync(CheckoutViewModel viewModel, int customerId, Dictionary<int, Medicine> medicines)
        {
            Prescription? prescription = null;

            if (!viewModel.CartItems.Any())
            {
                ModelState.AddModelError("", "Your cart is empty.");
            }

            if (viewModel.PrescriptionId.HasValue)
            {
                prescription = await _context.Prescriptions
                    .FirstOrDefaultAsync(p => p.PrescriptionId == viewModel.PrescriptionId.Value && p.CustomerId == customerId);

                if (prescription == null)
                {
                    ModelState.AddModelError(nameof(viewModel.PrescriptionId), "Please select one of your own uploaded prescriptions.");
                }
            }

            foreach (var group in viewModel.CartItems.GroupBy(i => i.MedicineId))
            {
                int quantity = group.Sum(i => i.Quantity);

                if (!medicines.TryGetValue(group.Key, out var medicine) || !medicine.IsActive)
                {
                    ModelState.AddModelError("", "One of the medicines in your order is no longer available.");
                    continue;
                }

                int available = MedicinePricing.GetSellableStock(medicine);
                if (available <= 0)
                {
                    ModelState.AddModelError("", $"{medicine.Name} is currently out of stock.");
                }
                else if (quantity > available)
                {
                    ModelState.AddModelError("", $"Only {available} unit(s) of {medicine.Name} are available.");
                }

                if (quantity > CartService.MaxQuantityPerItem)
                {
                    ModelState.AddModelError("", $"You can order at most {CartService.MaxQuantityPerItem} units of {medicine.Name}.");
                }

                if (MedicinePricing.GetCurrentPrice(medicine) <= 0)
                {
                    ModelState.AddModelError("", $"{medicine.Name} has no price set yet and cannot be ordered.");
                }

                if (medicine.IsPrescriptionRequired && prescription == null)
                {
                    ModelState.AddModelError(nameof(viewModel.PrescriptionId),
                        $"{medicine.Name} requires a prescription. Please upload one and select it below.");
                }
            }

            return prescription;
        }

        private async Task PopulatePrescriptionsAsync(CheckoutViewModel viewModel, int customerId)
        {
            var prescriptions = await _context.Prescriptions
                .AsNoTracking()
                .Where(p => p.CustomerId == customerId)
                .OrderByDescending(p => p.UploadedAt)
                .ToListAsync();

            viewModel.Prescriptions = prescriptions.Select(p => new SelectListItem
            {
                Value = p.PrescriptionId.ToString(),
                Text = $"Prescription #{p.PrescriptionId} - Uploaded: {p.UploadedAt:yyyy-MM-dd}",
                Selected = viewModel.PrescriptionId == p.PrescriptionId
            }).ToList();
        }

        #endregion
    }
}