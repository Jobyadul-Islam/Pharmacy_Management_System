using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using PharmacyManagement.Models.Validation;

namespace PharmacyManagement.Models.ViewModels.Prescription
{
    public class PrescriptionUploadViewModel
    {
        public int CustomerId { get; set; }

        [Required(ErrorMessage = "Please select a prescription file to upload.")]
        [AllowedImageFile(5)]
        public IFormFile PrescriptionFile { get; set; } = null!;

        public string? FileUrl { get; set; }

        [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
        public string? Notes { get; set; }
    }
}
