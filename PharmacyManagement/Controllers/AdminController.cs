using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Models;
using PharmacyManagement.Models.ViewModels.Admin;
using PharmacyManagement.Models.ViewModels.Medicine;
using PharmacyManagement.Models.ViewModels.Prescription;
using PharmacyManagement.Models.ViewModels.Purchase;
using PharmacyManagement.Models.ViewModels.Stock;
namespace PharmacyManagement.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AdminController(AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        #region Dashboard & Customer Management

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            try
            {
                var customerList = await _context.Customers
                    .AsNoTracking()
                    .Include(c => c.Purchases!)
                        .ThenInclude(p => p.PurchaseItems!)
                    .ToListAsync();

                var customers = customerList.Select(c => new CustomerListViewModel
                {
                    CustomerId = c.CustomerId,
                    Name = c.Name ?? "Unknown",
                    Email = c.Email ?? "",
                    Phone = c.Phone ?? "",
                    Address = c.Address ?? "",
                    TotalMedicinesBought = (c.Purchases ?? new List<Purchase>())
                        .SelectMany(p => p.PurchaseItems ?? Enumerable.Empty<PurchaseItem>())
                        .Sum(pi => pi.Quantity)
                }).ToList();

                var medicinesList = await _context.Medicines
                    .AsNoTracking()
                    .Include(m => m.Stocks!)
                    .Include(m => m.PurchaseItems!)
                    .ToListAsync();

                var salesStockData = new List<AdminSalesStockViewModel>();
                int totalStockQty = 0;

                foreach (var m in medicinesList)
                {
                    int unitsSold = m.PurchaseItems?.Sum(pi => pi.Quantity) ?? 0;
                    int unitsLeft = m.Stocks?.Sum(s => s.Quantity) ?? 0;
                    totalStockQty += unitsLeft;

                    decimal buyPrice = 0;
                    decimal sellPrice = 0;
                    var latestStock = m.Stocks?.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
                    if (latestStock != null)
                    {
                        buyPrice = latestStock.PurchasePrice;
                        sellPrice = latestStock.SellingPrice;
                    }

                    salesStockData.Add(new AdminSalesStockViewModel
                    {
                        MedicineId = m.MedicineId,
                        MedicineName = m.Name ?? "Unknown",
                        UnitsSold = unitsSold,
                        UnitsLeft = unitsLeft,
                        BuyPrice = buyPrice,
                        SellPrice = sellPrice
                    });
                }

                var viewModel = new AdminDashboardViewModel
                {
                    TotalCustomers = customers.Count,
                    TotalMedicinesStock = totalStockQty,
                    Customers = customers,
                    SalesStocks = salesStockData
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Admin Dashboard: {ex.Message}");
                return StatusCode(500, "Internal Server Error loading dashboard.");
            }
        }

        [HttpGet]
        [Route("Admin/CustomerPurchaseHistory/{id:int}")]
        public async Task<IActionResult> CustomerPurchaseHistory(int id)
        {
            try
            {
                var customer = await _context.Customers
                    .AsNoTracking()
                    .Include(c => c.Purchases!)
                        .ThenInclude(p => p.PurchaseItems!)
                            .ThenInclude(pi => pi.Medicine!)
                    .FirstOrDefaultAsync(c => c.CustomerId == id);

                if (customer == null)
                {
                    return NotFound($"Customer with ID {id} was not found.");
                }
                var purchases = (customer.Purchases ?? new List<Purchase>())
                    .OrderByDescending(p => p.PurchaseDate)
                    .Select(p => new PurchaseOrderViewModel
                    {
                        PurchaseId = p.PurchaseId,
                        PurchaseDate = p.PurchaseDate,
                        TotalAmount = p.TotalAmount,
                        PurchaseItems = (p.PurchaseItems ?? new List<PurchaseItem>())
                            .Select(pi => new PurchaseItemViewModel
                            {
                                PurchaseItemId = pi.PurchaseItemId,
                                MedicineId = pi.MedicineId,
                                MedicineName = pi.Medicine != null ? pi.Medicine.Name : "N/A",
                                Quantity = pi.Quantity,
                                UnitPrice = pi.UnitPrice,
                                TotalPrice = pi.Quantity * pi.UnitPrice
                            }).ToList()
                    }).ToList();

                var viewModel = new CustomerPurchaseHistoryAdminViewModel
                {
                    CustomerId = customer.CustomerId,
                    CustomerName = customer.Name ?? "N/A",
                    Purchases = purchases
                };

                return PartialView("/Views/Admin/CustomerPurchaseHistory.cshtml", viewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CustomerPurchaseHistory: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return StatusCode(500, $"Internal Error: {ex.Message}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            var customer = await _context.Customers
                .Include(c => c.Purchases!)
                .Include(c => c.Prescriptions!)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer != null)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.CustomerId == id);
                if (user != null) _context.Users.Remove(user);

                if (customer.Purchases?.Any() == true)
                {
                    var purchaseIds = customer.Purchases.Select(p => p.PurchaseId).ToList();
                    var purchaseItems = _context.PurchaseItems.Where(pi => purchaseIds.Contains(pi.PurchaseId));
                    _context.PurchaseItems.RemoveRange(purchaseItems);
                    _context.Purchases.RemoveRange(customer.Purchases);
                }

                if (customer.Prescriptions?.Any() == true)
                {
                    foreach (var prescription in customer.Prescriptions)
                    {
                        if (!string.IsNullOrEmpty(prescription.FileUrl))
                        {
                            DeleteFile(prescription.FileUrl);
                        }
                    }
                    _context.Prescriptions.RemoveRange(customer.Prescriptions);
                }

                _context.Customers.Remove(customer);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Dashboard));
        }

        #endregion

        #region Order Management

        [HttpGet]
        public async Task<IActionResult> Orders(string? status = null)
        {
            var query = _context.Purchases.AsNoTracking();

            if (status == "Pending" || status == "Completed")
            {
                query = query.Where(p => p.Status == status);
            }

            var orders = await query
                .OrderByDescending(p => p.PurchaseDate)
                .Select(p => new PurchaseViewModel
                {
                    PurchaseId = p.PurchaseId,
                    CustomerId = p.CustomerId,
                    CustomerName = p.Customer != null ? p.Customer.Name : "Unknown",
                    PrescriptionId = p.PrescriptionId,
                    PaymentMethod = p.PaymentMethod,
                    DeliveryMethod = p.DeliveryMethod,
                    DeliveryAddress = p.DeliveryAddress,
                    Status = p.Status,
                    TotalAmount = p.TotalAmount,
                    PurchaseDate = p.PurchaseDate,
                    PurchaseItems = p.PurchaseItems.Select(pi => new PurchaseItemViewModel
                    {
                        MedicineName = pi.Medicine != null ? pi.Medicine.Name : "Medicine",
                        Quantity = pi.Quantity,
                        UnitPrice = pi.UnitPrice,
                        TotalPrice = pi.UnitPrice * pi.Quantity
                    }).ToList()
                })
                .ToListAsync();

            ViewBag.StatusFilter = status;
            return View("~/Views/Admin/Orders.cshtml", orders);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteOrder(int id, string? status = null)
        {
            var purchase = await _context.Purchases.FindAsync(id);

            if (purchase == null)
            {
                TempData["OrderError"] = $"Order #{id} was not found.";
            }
            else if (purchase.Status == "Completed")
            {
                TempData["OrderError"] = $"Order #{id} is already completed.";
            }
            else
            {
                purchase.Status = "Completed";
                await _context.SaveChangesAsync();
                TempData["OrderMessage"] = $"Order #{id} was marked as completed.";
            }

            return RedirectToAction(nameof(Orders), new { status });
        }

        #endregion

        #region Medicine Management

        [HttpGet]
        public async Task<IActionResult> Medicines()
        {
            var medList = await _context.Medicines
                .AsNoTracking()
                .Include(m => m.Category!)
                .Include(m => m.Stocks!)
                .ToListAsync();

            var medicines = medList.Select(m => new MedicineViewModel
            {
                MedicineId = m.MedicineId,
                Name = m.Name ?? "",
                GenericName = m.GenericName ?? "",
                BrandName = m.BrandName ?? "",
                Strength = m.Strength ?? "",
                DosageForm = m.DosageForm ?? "",
                CategoryName = m.Category?.Name ?? "Uncategorized",
                CategoryId = m.CategoryId,
                Manufacturer = m.Manufacturer ?? "",
                ImageUrl = m.ImageUrl ?? "",
                IsPrescriptionRequired = m.IsPrescriptionRequired,
                Description = m.Description ?? "",
                IsActive = m.IsActive,
                TotalStockQuantity = m.Stocks?.Sum(s => s.Quantity) ?? 0,
                LatestSellingPrice = m.Stocks?.OrderByDescending(s => s.CreatedAt).FirstOrDefault()?.SellingPrice ?? 0
            }).ToList();

            return View("~/Views/Admin/Medicine/Index.cshtml", medicines);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var viewModel = new MedicineCreateViewModel
            {
                Categories = await PopulateCategoriesSelectList()
            };
            return View("~/Views/Admin/Medicine/Create.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MedicineCreateViewModel model)
        {
            await ValidateMedicineAsync(model.Name, model.CategoryId, medicineId: null);

            if (ModelState.IsValid)
            {
                string imagePath = string.Empty;
                if (model.ImageFile != null)
                {
                    imagePath = await UploadFileAsync(model.ImageFile, "uploads/medicines");
                }

                var medicine = new Medicine
                {
                    Name = model.Name.Trim(),
                    GenericName = model.GenericName?.Trim(),
                    BrandName = model.BrandName?.Trim(),
                    CategoryId = model.CategoryId,
                    DosageForm = model.DosageForm.Trim(),
                    Strength = model.Strength?.Trim(),
                    Manufacturer = model.Manufacturer?.Trim(),
                    Description = model.Description?.Trim(),
                    IsPrescriptionRequired = model.IsPrescriptionRequired,
                    ImageUrl = imagePath,
                    IsActive = true
                };

                _context.Medicines.Add(medicine);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Medicines));
            }

            model.Categories = await PopulateCategoriesSelectList(model.CategoryId);
            return View("~/Views/Admin/Medicine/Create.cshtml", model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var medicine = await _context.Medicines.FindAsync(id);
            if (medicine == null) return NotFound();

            var viewModel = new MedicineEditViewModel
            {
                MedicineId = medicine.MedicineId,
                Name = medicine.Name,
                GenericName = medicine.GenericName,
                BrandName = medicine.BrandName,
                CategoryId = medicine.CategoryId,
                DosageForm = medicine.DosageForm,
                Strength = medicine.Strength,
                Manufacturer = medicine.Manufacturer,
                Description = medicine.Description,
                IsPrescriptionRequired = medicine.IsPrescriptionRequired,
                ImageUrl = medicine.ImageUrl,
                IsActive = medicine.IsActive,
                Categories = await PopulateCategoriesSelectList(medicine.CategoryId)
            };

            return View("~/Views/Admin/Medicine/Edit.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(MedicineEditViewModel model)
        {
            await ValidateMedicineAsync(model.Name, model.CategoryId, model.MedicineId);

            if (ModelState.IsValid)
            {
                var medicine = await _context.Medicines.FindAsync(model.MedicineId);
                if (medicine == null) return NotFound();

                if (model.ImageFile != null)
                {
                    DeleteFile(medicine.ImageUrl);
                    medicine.ImageUrl = await UploadFileAsync(model.ImageFile, "uploads/medicines");
                }

                medicine.Name = model.Name.Trim();
                medicine.GenericName = model.GenericName?.Trim();
                medicine.BrandName = model.BrandName?.Trim();
                medicine.CategoryId = model.CategoryId;
                medicine.DosageForm = model.DosageForm.Trim();
                medicine.Strength = model.Strength?.Trim();
                medicine.Manufacturer = model.Manufacturer?.Trim();
                medicine.Description = model.Description?.Trim();
                medicine.IsPrescriptionRequired = model.IsPrescriptionRequired;
                medicine.IsActive = model.IsActive;

                _context.Medicines.Update(medicine);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Medicines));
            }

            model.Categories = await PopulateCategoriesSelectList(model.CategoryId);
            return View("~/Views/Admin/Medicine/Edit.cshtml", model);
        }

        [HttpGet]
        public async Task<IActionResult> DeleteMedicine(int id)
        {
            var medicine = await _context.Medicines
                .AsNoTracking()
                .Include(m => m.Category!)
                .FirstOrDefaultAsync(m => m.MedicineId == id);

            if (medicine == null) return NotFound();

            return View("~/Views/Admin/Medicine/Delete.cshtml", medicine);
        }

        [HttpPost, ActionName("DeleteMedicine")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMedicineConfirmed(int medicineId)
        {
            var medicine = await _context.Medicines
                .Include(m => m.Stocks!)
                .FirstOrDefaultAsync(m => m.MedicineId == medicineId);

            if (medicine != null)
            {
                DeleteFile(medicine.ImageUrl);

                if (medicine.Stocks != null && medicine.Stocks.Any())
                {
                    _context.Stocks.RemoveRange(medicine.Stocks);
                }

                _context.Medicines.Remove(medicine);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Medicines));
        }

        #endregion

        #region Stock Management

        [HttpGet]
        public async Task<IActionResult> Stocks()
        {
            var stockList = await _context.Stocks
                .AsNoTracking()
                .Include(s => s.Medicine!)
                .ToListAsync();

            var stocks = stockList.Select(s => new StockViewModel
            {
                StockId = s.StockId,
                MedicineId = s.MedicineId,
                MedicineName = s.Medicine?.Name ?? "Unknown",
                BatchNumber = s.BatchNumber ?? "",
                Quantity = s.Quantity,
                PurchasePrice = s.PurchasePrice,
                SellingPrice = s.SellingPrice,
                ExpiryDate = s.ExpiryDate,
                CreatedAt = s.CreatedAt
            }).ToList();

            return View("~/Views/Admin/Stock/Index.cshtml", stocks);
        }

        [HttpGet]
        public async Task<IActionResult> AddStock()
        {
            var viewModel = new StockCreateEditViewModel
            {
                Medicines = await PopulateMedicinesSelectList()
            };
            return View("~/Views/Admin/Stock/AddStock.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddStock(StockCreateEditViewModel model)
        {
            model.StockId = 0;
            await ValidateStockAsync(model);

            if (ModelState.IsValid)
            {
                var stock = new Stock
                {
                    MedicineId = model.MedicineId,
                    BatchNumber = model.BatchNumber.Trim(),
                    Quantity = model.Quantity!.Value,
                    PurchasePrice = model.PurchasePrice!.Value,
                    SellingPrice = model.SellingPrice!.Value,
                    ManufacturingDate = model.ManufacturingDate!.Value.Date,
                    ExpiryDate = model.ExpiryDate!.Value.Date,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Stocks.Add(stock);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Stocks));
            }

            model.Medicines = await PopulateMedicinesSelectList(model.MedicineId);
            return View("~/Views/Admin/Stock/AddStock.cshtml", model);
        }

        [HttpGet]
        public async Task<IActionResult> EditStock(int id)
        {
            var stock = await _context.Stocks.FindAsync(id);
            if (stock == null) return NotFound();

            var viewModel = new StockCreateEditViewModel
            {
                StockId = stock.StockId,
                MedicineId = stock.MedicineId,
                BatchNumber = stock.BatchNumber,
                Quantity = stock.Quantity,
                PurchasePrice = stock.PurchasePrice,
                SellingPrice = stock.SellingPrice,
                // Older rows were saved without a manufacturing date; leave it blank so the admin fills it in.
                ManufacturingDate = stock.ManufacturingDate == default ? null : stock.ManufacturingDate,
                ExpiryDate = stock.ExpiryDate,
                Medicines = await PopulateMedicinesSelectList(stock.MedicineId, includeInactiveId: stock.MedicineId)
            };

            return View("~/Views/Admin/Stock/Edit.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditStock(StockCreateEditViewModel model)
        {
            var stock = await _context.Stocks.FindAsync(model.StockId);
            if (stock == null) return NotFound();

            await ValidateStockAsync(model, existingMedicineId: stock.MedicineId);

            if (ModelState.IsValid)
            {
                stock.MedicineId = model.MedicineId;
                stock.BatchNumber = model.BatchNumber.Trim();
                stock.Quantity = model.Quantity!.Value;
                stock.PurchasePrice = model.PurchasePrice!.Value;
                stock.SellingPrice = model.SellingPrice!.Value;
                stock.ManufacturingDate = model.ManufacturingDate!.Value.Date;
                stock.ExpiryDate = model.ExpiryDate!.Value.Date;

                _context.Stocks.Update(stock);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Stocks));
            }

            model.Medicines = await PopulateMedicinesSelectList(model.MedicineId, includeInactiveId: stock.MedicineId);
            return View("~/Views/Admin/Stock/Edit.cshtml", model);
        }

        [HttpGet]
        public async Task<IActionResult> DeleteStock(int id)
        {
            var stock = await _context.Stocks
                .AsNoTracking()
                .Include(s => s.Medicine!)
                .FirstOrDefaultAsync(s => s.StockId == id);

            if (stock == null) return NotFound();

            return View("~/Views/Admin/Stock/Delete.cshtml", stock);
        }

        [HttpPost, ActionName("DeleteStock")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStockConfirmed(int stockId)
        {
            var stock = await _context.Stocks.FindAsync(stockId);
            if (stock != null)
            {
                _context.Stocks.Remove(stock);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Stocks));
        }

        #endregion

        #region Prescription Management

        [HttpGet]
        public async Task<IActionResult> Prescriptions()
        {
            var presList = await _context.Prescriptions
                .AsNoTracking()
                .Include(p => p.Customer!)
                .OrderByDescending(p => p.UploadedAt)
                .ToListAsync();

            var allMedicines = await _context.Medicines
                .AsNoTracking()
                .Include(m => m.Stocks!)
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

                string stockStatus = "No Medicines Detected";
                if (matchedItems.Any())
                {
                    bool allAvailable = matchedItems.All(m => m.IsAvailable);
                    bool anyAvailable = matchedItems.Any(m => m.IsAvailable);

                    if (allAvailable) stockStatus = "All Available";
                    else if (anyAvailable) stockStatus = "Partially Available";
                    else stockStatus = "Out of Stock";
                }

                prescriptions.Add(new PrescriptionViewModel
                {
                    PrescriptionId = p.PrescriptionId,
                    CustomerId = p.CustomerId,
                    CustomerName = p.Customer?.Name ?? "Unknown Customer",
                    FileUrl = p.FileUrl ?? string.Empty,
                    Notes = p.Notes,
                    ExtractedMedicines = p.ExtractedMedicines,
                    Status = stockStatus,
                    UploadedAt = p.UploadedAt,
                    MatchedItems = matchedItems
                });
            }

            return View("~/Views/Admin/Prescriptions.cshtml", prescriptions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePrescription(int id)
        {
            var prescription = await _context.Prescriptions.FindAsync(id);

            if (prescription != null)
            {
                DeleteFile(prescription.FileUrl);
                _context.Prescriptions.Remove(prescription);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Prescriptions));
        }

        #endregion

        #region Helper Methods

        private async Task<IEnumerable<SelectListItem>> PopulateCategoriesSelectList(int? selectedId = null)
        {
            var categories = await _context.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
            return categories.Select(c => new SelectListItem
            {
                Value = c.CategoryId.ToString(),
                Text = c.Name,
                Selected = selectedId.HasValue && c.CategoryId == selectedId.Value
            });
        }

        // Server-side checks for medicine forms that need the database.
        private async Task ValidateMedicineAsync(string? name, int categoryId, int? medicineId)
        {
            if (categoryId > 0 && !await _context.Categories.AnyAsync(c => c.CategoryId == categoryId))
            {
                ModelState.AddModelError("CategoryId", "The selected category does not exist.");
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var trimmedName = name.Trim();
                bool duplicate = await _context.Medicines.AnyAsync(m =>
                    m.Name == trimmedName && (medicineId == null || m.MedicineId != medicineId));
                if (duplicate)
                {
                    ModelState.AddModelError("Name", "A medicine with this name already exists.");
                }
            }
        }

        // Server-side checks for stock forms that need the database.
        private async Task ValidateStockAsync(StockCreateEditViewModel model, int? existingMedicineId = null)
        {
            if (model.MedicineId <= 0)
            {
                return;
            }

            var medicine = await _context.Medicines.AsNoTracking()
                .FirstOrDefaultAsync(m => m.MedicineId == model.MedicineId);

            if (medicine == null)
            {
                ModelState.AddModelError(nameof(model.MedicineId), "The selected medicine does not exist.");
                return;
            }

            // New stock can only be added for active medicines; an existing batch may keep its (now inactive) medicine.
            if (!medicine.IsActive && model.MedicineId != existingMedicineId)
            {
                ModelState.AddModelError(nameof(model.MedicineId), "You cannot add stock for an inactive medicine.");
            }

            if (!string.IsNullOrWhiteSpace(model.BatchNumber))
            {
                var batch = model.BatchNumber.Trim();
                bool duplicateBatch = await _context.Stocks.AnyAsync(s =>
                    s.MedicineId == model.MedicineId && s.BatchNumber == batch && s.StockId != model.StockId);
                if (duplicateBatch)
                {
                    ModelState.AddModelError(nameof(model.BatchNumber), "This batch number already exists for the selected medicine.");
                }
            }
        }

        private async Task<IEnumerable<SelectListItem>> PopulateMedicinesSelectList(int? selectedId = null, int? includeInactiveId = null)
        {
            var medicines = await _context.Medicines.AsNoTracking()
                .Where(m => m.IsActive || m.MedicineId == includeInactiveId)
                .OrderBy(m => m.Name)
                .ToListAsync();
            return medicines.Select(m => new SelectListItem
            {
                Value = m.MedicineId.ToString(),
                Text = m.Name,
                Selected = selectedId.HasValue && m.MedicineId == selectedId.Value
            });
        }

        private async Task<string> UploadFileAsync(IFormFile file, string folderPath)
        {
            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, folderPath);
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            // Never use the user's file name on disk; keep only the (already validated) extension.
            string uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            return $"/{folderPath}/{uniqueFileName}";
        }

        private void DeleteFile(string? relativeUrl)
        {
            if (string.IsNullOrEmpty(relativeUrl)) return;

            var filePath = Path.Combine(_webHostEnvironment.WebRootPath, relativeUrl.TrimStart('/'));
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }

        #endregion
    }
}