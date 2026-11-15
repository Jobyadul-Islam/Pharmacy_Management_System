using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using PharmacyManagement.Models.Validation;

namespace PharmacyManagement.Models.ViewModels.Medicine
{
    public class MedicineCreateViewModel
    {
        [Required(ErrorMessage = "Medicine name is required.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Medicine name must be between 2 and 150 characters.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(150)]
        public string? GenericName { get; set; }

        [StringLength(100)]
        public string? BrandName { get; set; }

        [StringLength(50)]
        public string? Strength { get; set; }

        [Required(ErrorMessage = "Dosage form is required.")]
        [StringLength(50)]
        public string DosageForm { get; set; } = string.Empty;

        [Required(ErrorMessage = "Category is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a category.")]
        public int CategoryId { get; set; }

        [StringLength(200)]
        public string? Manufacturer { get; set; }

        [AllowedImageFile(2)]
        public IFormFile? ImageFile { get; set; }

        public bool IsPrescriptionRequired { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public IEnumerable<SelectListItem>? Categories { get; set; }
    }
}