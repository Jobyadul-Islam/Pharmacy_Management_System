using System.ComponentModel.DataAnnotations;

namespace PharmacyManagement.Models.ViewModels.Category
{
    public class CategoryCreateEditViewModel
    {
        public int CategoryId { get; set; }

        [Required(ErrorMessage = "Category name is required.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Category name must be between 2 and 100 characters.")]
        [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9 &,'()/-]*$",
            ErrorMessage = "Category name can only contain letters, numbers, spaces and & , ' ( ) / -")]
        [Display(Name = "Category Name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string? Description { get; set; }
    }
}
