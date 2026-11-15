using System.Security.Claims;
using System.Text.Json;

namespace PharmacyManagement.Services
{
    /// <summary>
    /// A shopping cart stored in a browser cookie. It only holds medicine IDs and quantities;
    /// names, prices and stock are always read from the database, so editing the cookie can't
    /// change what a customer pays. The cart is tied to the logged-in user, so if two people
    /// use the same browser they never see each other's cart.
    /// </summary>
    public class CartService
    {
        public const string CookieName = "TrexCart";
        public const int MaxQuantityPerItem = 100;
        public const int MaxDistinctItems = 50;

        private readonly IHttpContextAccessor _httpContextAccessor;

        public CartService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public class CartLine
        {
            public int MedicineId { get; set; }
            public int Quantity { get; set; }
        }

        private class CartData
        {
            public string Owner { get; set; } = string.Empty;
            public List<CartLine> Items { get; set; } = new();
        }

        private HttpContext? Context => _httpContextAccessor.HttpContext;

        private string? CurrentUserId => Context?.User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Changes made during this request. The service is scoped (one per request), and the request
        // cookie still holds the old value after we write a new one, so we keep our own copy.
        private List<CartLine>? _current;

        public List<CartLine> GetItems()
        {
            _current ??= ReadFromCookie();
            return _current.Select(i => new CartLine { MedicineId = i.MedicineId, Quantity = i.Quantity }).ToList();
        }

        private List<CartLine> ReadFromCookie()
        {
            var userId = CurrentUserId;
            var raw = Context?.Request.Cookies[CookieName];
            if (userId == null || string.IsNullOrEmpty(raw))
            {
                return new List<CartLine>();
            }

            try
            {
                var data = JsonSerializer.Deserialize<CartData>(raw);
                if (data == null || data.Owner != userId)
                {
                    return new List<CartLine>();
                }

                // Clean anything invalid that may have been put into the cookie.
                return data.Items
                    .Where(i => i.MedicineId > 0 && i.Quantity > 0)
                    .GroupBy(i => i.MedicineId)
                    .Select(g => new CartLine { MedicineId = g.Key, Quantity = Math.Min(g.Sum(i => i.Quantity), MaxQuantityPerItem) })
                    .Take(MaxDistinctItems)
                    .ToList();
            }
            catch (JsonException)
            {
                return new List<CartLine>();
            }
        }

        public int GetTotalQuantity() => GetItems().Sum(i => i.Quantity);

        public int GetQuantity(int medicineId) => GetItems().FirstOrDefault(i => i.MedicineId == medicineId)?.Quantity ?? 0;

        public void SetQuantity(int medicineId, int quantity)
        {
            var items = GetItems();
            var line = items.FirstOrDefault(i => i.MedicineId == medicineId);

            if (quantity <= 0)
            {
                if (line != null) items.Remove(line);
            }
            else if (line != null)
            {
                line.Quantity = Math.Min(quantity, MaxQuantityPerItem);
            }
            else if (items.Count < MaxDistinctItems)
            {
                items.Add(new CartLine { MedicineId = medicineId, Quantity = Math.Min(quantity, MaxQuantityPerItem) });
            }

            Save(items);
        }

        public void Remove(int medicineId) => SetQuantity(medicineId, 0);

        public void Clear()
        {
            _current = new List<CartLine>();
            Context?.Response.Cookies.Delete(CookieName);
        }

        private void Save(List<CartLine> items)
        {
            var userId = CurrentUserId;
            if (Context == null || userId == null) return;

            _current = items;

            if (items.Count == 0)
            {
                Clear();
                return;
            }

            var json = JsonSerializer.Serialize(new CartData { Owner = userId, Items = items });
            Context.Response.Cookies.Append(CookieName, json, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Context.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
        }
    }
}
