using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PharmacyManagement.Models;
using PharmacyManagement.Models.ViewModels.Customer;

namespace PharmacyManagement.Controllers
{
    public class AuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PharmacyManagement.Data.AppDbContext _context;

        public AuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            PharmacyManagement.Data.AppDbContext context)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            if (User != null)
            {
                if (User.Identity != null)
                {
                    if (User.Identity.IsAuthenticated == true)
                    {
                        if (string.IsNullOrEmpty(returnUrl) == false)
                        {
                            if (Url.IsLocalUrl(returnUrl) == true)
                            {
                                return Redirect(returnUrl);
                            }
                        }

                        var loggedInUser = await _userManager.GetUserAsync(User);
                        if (loggedInUser != null)
                        {
                            var isAdmin = await _userManager.IsInRoleAsync(loggedInUser, "Admin");
                            if (isAdmin == true)
                            {
                                return RedirectToAction("Dashboard", "Admin");
                            }
                        }

                        return RedirectToAction("Index", "Home");
                    }
                }
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(CustomerLoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (ModelState.IsValid == false)
            {
                return View(model);
            }

            var userToLogin = await _userManager.FindByEmailAsync(model.Email);
            if (userToLogin == null)
            {
                ModelState.AddModelError("", "Invalid login attempt.");
                return View(model);
            }

            var signInResult = await _signInManager.PasswordSignInAsync(
                userToLogin.UserName!,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true);

            if (signInResult.Succeeded == true)
            {
                var checkAdminRole = await _userManager.IsInRoleAsync(userToLogin, "Admin");
                if (checkAdminRole == true)
                {
                    return RedirectToAction("Dashboard", "Admin");
                }

                if (string.IsNullOrEmpty(returnUrl) == false)
                {
                    if (Url.IsLocalUrl(returnUrl) == true)
                    {
                        return Redirect(returnUrl);
                    }
                }

                return RedirectToAction("Index", "Home");
            }

            if (signInResult.IsLockedOut)
            {
                ModelState.AddModelError("", "This account is locked because of too many failed login attempts. Please try again in 15 minutes.");
                return View(model);
            }

            ModelState.AddModelError("", "Invalid login attempt.");
            return View(model);
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(CustomerRegisterViewModel model)
        {
            if (ModelState.IsValid == false)
            {
                return View(model);
            }

            model.Email = model.Email.Trim();

            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
                return View(model);
            }

            Customer newCustomer = new Customer();
            newCustomer.Name = model.Name.Trim();
            newCustomer.Age = model.Age!.Value;
            newCustomer.Gender = model.Gender;
            newCustomer.Phone = model.Phone?.Trim();
            newCustomer.Email = model.Email;
            newCustomer.Address = model.Address?.Trim();
            newCustomer.CreatedAt = DateTime.UtcNow;

            _context.Customers.Add(newCustomer);
            await _context.SaveChangesAsync();

            ApplicationUser newUser = new ApplicationUser();
            newUser.UserName = model.Email;
            newUser.Email = model.Email;
            newUser.FullName = newCustomer.Name;
            newUser.CustomerId = newCustomer.CustomerId;

            var createResult = await _userManager.CreateAsync(newUser, model.Password);

            if (createResult.Succeeded == true)
            {
                await _userManager.AddToRoleAsync(newUser, "Customer");
                await _signInManager.SignInAsync(newUser, isPersistent: false);

                return RedirectToAction("Index", "Home");
            }

            _context.Customers.Remove(newCustomer);
            await _context.SaveChangesAsync();

            foreach (var err in createResult.Errors)
            {
                ModelState.AddModelError(string.Empty, err.Description);
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            Response.Cookies.Delete(PharmacyManagement.Services.CartService.CookieName);
            return RedirectToAction("Login", "Auth");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (string.IsNullOrEmpty(returnUrl) == false)
            {
                if (Url.IsLocalUrl(returnUrl) == true)
                {
                    return Redirect(returnUrl);
                }
            }
            return RedirectToAction("Index", "Home");
        }
    }
}