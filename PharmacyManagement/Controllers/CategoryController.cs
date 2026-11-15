using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Models;
using PharmacyManagement.Models.ViewModels.Category;

namespace PharmacyManagement.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CategoryController : Controller
    {
        private readonly AppDbContext _context;

        public CategoryController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryViewModel
                {
                    CategoryId = c.CategoryId,
                    Name = c.Name,
                    Description = c.Description,
                    MedicineCount = c.Medicines.Count
                })
                .ToListAsync();

            return View(categories);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View("Form", new CategoryCreateEditViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CategoryCreateEditViewModel model)
        {
            model.CategoryId = 0;
            await ValidateUniqueNameAsync(model);

            if (!ModelState.IsValid)
            {
                return View("Form", model);
            }

            _context.Categories.Add(new Category
            {
                Name = model.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim()
            });
            await _context.SaveChangesAsync();

            TempData["CategoryMessage"] = $"Category \"{model.Name.Trim()}\" was added.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var category = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.CategoryId == id);
            if (category == null) return NotFound();

            return View("Form", new CategoryCreateEditViewModel
            {
                CategoryId = category.CategoryId,
                Name = category.Name,
                Description = category.Description
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CategoryCreateEditViewModel model)
        {
            var category = await _context.Categories.FindAsync(model.CategoryId);
            if (category == null) return NotFound();

            await ValidateUniqueNameAsync(model);

            if (!ModelState.IsValid)
            {
                return View("Form", model);
            }

            category.Name = model.Name.Trim();
            category.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            await _context.SaveChangesAsync();

            TempData["CategoryMessage"] = $"Category \"{category.Name}\" was updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _context.Categories
                .Include(c => c.Medicines)
                .FirstOrDefaultAsync(c => c.CategoryId == id);

            if (category == null) return NotFound();

            // Medicines must always have a category, so a category that is in use can't be deleted.
            if (category.Medicines.Any())
            {
                TempData["CategoryError"] =
                    $"\"{category.Name}\" cannot be deleted because {category.Medicines.Count} medicine(s) use it. Move those medicines to another category first.";
                return RedirectToAction(nameof(Index));
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            TempData["CategoryMessage"] = $"Category \"{category.Name}\" was deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task ValidateUniqueNameAsync(CategoryCreateEditViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Name)) return;

            var name = model.Name.Trim();
            bool exists = await _context.Categories.AnyAsync(c => c.Name == name && c.CategoryId != model.CategoryId);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.Name), "A category with this name already exists.");
            }
        }
    }
}
