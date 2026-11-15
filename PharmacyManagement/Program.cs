using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Models;
using PharmacyManagement.Services;

var builder = WebApplication.CreateBuilder(args);

// Register services and database context
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure ASP.NET Core Identity options
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Must match ValidationPatterns.Password: upper + lower case letters, a number and a symbol.
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;

    // Lock an account for 15 minutes after 5 wrong passwords in a row (stops password guessing).
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// Configure authentication cookie and login paths
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Auth/Login";
    options.AccessDeniedPath = "/Auth/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
});

// GROQ AI SERVICE CONFIGURATION
builder.Services.AddHttpClient("GroqClient", client =>
{
    client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
    client.DefaultRequestHeaders.Add("Authorization", "Bearer ");
});

builder.Services.AddScoped<GroqPrescriptionService>();

// Shopping cart (stored in a cookie, see CartService)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CartService>();

var builderApp = builder.Build();

// Seed database, roles, and default admin user
using (var scope = builderApp.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync();

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        string[] roleNames = { "Admin", "Customer" };

        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        // Seed common medicine categories the first time the app runs (only when there are none),
        // so the admin can add medicines right away. More can be added from Admin > Categories.
        if (!await context.Categories.AnyAsync())
        {
            string[] defaultCategories =
            {
                "Pain Relief & Fever", "Antibiotics", "Gastric & Antacids", "Allergy & Antihistamines",
                "Cold & Cough", "Diabetes", "Heart & Blood Pressure", "Vitamins & Supplements",
                "Skin Care", "Eye & Ear Care", "Asthma & Respiratory", "Baby & Child Care", "First Aid"
            };

            context.Categories.AddRange(defaultCategories.Select(name => new Category { Name = name }));
            await context.SaveChangesAsync();
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var seedLogger = services.GetRequiredService<ILogger<Program>>();

        // Admin credentials come from configuration (user-secrets in development, environment
        // variables in production), never from source code.
        string? adminEmail = builder.Configuration["AdminSettings:Email"];
        string? adminPassword = builder.Configuration["AdminSettings:Password"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            seedLogger.LogWarning(
                "AdminSettings:Email / AdminSettings:Password are not configured, so no admin account was created or updated. " +
                "Set them with: dotnet user-secrets set \"AdminSettings:Password\" \"<password>\"");
        }
        else
        {
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "System Administrator",
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(adminUser, adminPassword);
                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
                else
                {
                    seedLogger.LogError("Could not create the admin account: {Errors}",
                        string.Join(" ", createResult.Errors.Select(e => e.Description)));
                }
            }
            else
            {
                if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }

                // AdminSettings:Password (kept in user-secrets, not in source code) is the source of truth for the
                // admin password. If it was changed, update the admin account to match. This is also how the admin
                // password is changed: run `dotnet user-secrets set "AdminSettings:Password" "<new>"` and restart.
                if (!await userManager.CheckPasswordAsync(adminUser, adminPassword))
                {
                    var resetToken = await userManager.GeneratePasswordResetTokenAsync(adminUser);
                    var resetResult = await userManager.ResetPasswordAsync(adminUser, resetToken, adminPassword);
                    if (resetResult.Succeeded)
                    {
                        seedLogger.LogInformation("The admin password was updated to match AdminSettings:Password.");
                    }
                    else
                    {
                        seedLogger.LogError("Could not update the admin password (does it meet the password rules?): {Errors}",
                            string.Join(" ", resetResult.Errors.Select(e => e.Description)));
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database or seeding user data.");
    }
}

// Configure HTTP request pipeline
if (!builderApp.Environment.IsDevelopment())
{
    builderApp.UseExceptionHandler("/Home/Error");
    builderApp.UseHsts();
}

builderApp.UseHttpsRedirection();
builderApp.UseStaticFiles();
builderApp.UseRouting();

// Authentication and Authorization middleware
builderApp.UseAuthentication();
builderApp.UseAuthorization();

builderApp.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

builderApp.Run();