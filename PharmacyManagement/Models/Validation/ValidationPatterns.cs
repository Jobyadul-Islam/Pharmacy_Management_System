namespace PharmacyManagement.Models.Validation
{
    /// <summary>
    /// Shared regular expressions for [RegularExpression] attributes.
    /// They are written to work the same in .NET (server) and JavaScript (client-side jQuery validation).
    /// </summary>
    public static class ValidationPatterns
    {
        // Letters (English, accented Latin, Bangla), spaces, dots, apostrophes and hyphens. Must start with a letter.
        public const string PersonName = @"^[A-Za-zÀ-ɏঀ-৿][A-Za-zÀ-ɏঀ-৿ .'-]*$";
        public const string PersonNameMessage = "Name can only contain letters, spaces, dots, apostrophes and hyphens.";

        // Optional leading +, then 7-15 digits (spaces and hyphens allowed between digits). e.g. 01712345678, +880 1712-345678
        public const string Phone = @"^\+?[0-9](?:[ -]?[0-9]){6,14}$";
        public const string PhoneMessage = "Enter a valid phone number (7-15 digits, e.g. 01712345678 or +8801712345678).";

        // Something@something.tld - stricter than [EmailAddress], which accepts "a@b".
        public const string Email = @"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$";
        public const string EmailMessage = "Enter a valid email address (e.g. name@example.com).";

        public const string Gender = "^(Male|Female|Other)$";
        public const string GenderMessage = "Please select a valid gender.";

        // At least one lowercase letter, one uppercase letter, one digit and one symbol (any non-letter/non-digit, e.g. @ # $ ! %).
        public const string Password = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).+$";
        public const string PasswordMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number and one symbol (e.g. @ # $ ! %).";

        // Letters, digits, hyphens and slashes, e.g. BT-2026/01
        public const string BatchNumber = @"^[A-Za-z0-9][A-Za-z0-9\-/]*$";
        public const string BatchNumberMessage = "Batch number can only contain letters, numbers, hyphens and slashes.";

        public const string PaymentMethod = "^(CashOnDelivery|CreditCard|OnlineBanking)$";
        public const string DeliveryMethod = "^(HomeDelivery|StorePickup)$";
    }
}
