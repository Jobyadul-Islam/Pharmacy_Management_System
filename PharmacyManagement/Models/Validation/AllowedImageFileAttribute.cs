using System.ComponentModel.DataAnnotations;

namespace PharmacyManagement.Models.Validation
{
    /// <summary>
    /// Validates an uploaded image: allowed extension, maximum size, and that the
    /// file's first bytes really are a JPEG/PNG/WEBP image (not a renamed .exe or .html).
    /// A null file is treated as valid; combine with [Required] when a file is mandatory.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class AllowedImageFileAttribute : ValidationAttribute
    {
        public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".webp" };

        public int MaxSizeInMb { get; }

        public AllowedImageFileAttribute(int maxSizeInMb = 5)
        {
            MaxSizeInMb = maxSizeInMb;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not IFormFile file)
            {
                return ValidationResult.Success;
            }

            var memberNames = new[] { validationContext.MemberName ?? string.Empty };

            if (file.Length == 0)
            {
                return new ValidationResult("The selected file is empty.", memberNames);
            }

            if (file.Length > MaxSizeInMb * 1024L * 1024L)
            {
                return new ValidationResult($"The file must be {MaxSizeInMb} MB or smaller.", memberNames);
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!Extensions.Contains(extension))
            {
                return new ValidationResult("Only JPG, JPEG, PNG or WEBP images are allowed.", memberNames);
            }

            if (!HasValidImageSignature(file))
            {
                return new ValidationResult("The file is not a valid image.", memberNames);
            }

            return ValidationResult.Success;
        }

        private static bool HasValidImageSignature(IFormFile file)
        {
            var header = new byte[12];
            using (var stream = file.OpenReadStream())
            {
                int read = stream.Read(header, 0, header.Length);
                if (read < header.Length)
                {
                    return false;
                }
            }

            bool isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            bool isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
            bool isWebp = header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                       && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P';

            return isJpeg || isPng || isWebp;
        }

        public static string GetMimeType(string fileName)
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
        }
    }
}
