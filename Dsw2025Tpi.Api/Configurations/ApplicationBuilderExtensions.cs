using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using System.Text.Json;
using Dsw2025Tpi.Data.Helper;

namespace Dsw2025Tpi.Api.Configuration
{
    public static class ApplicationBuilderExtensions
    {
        public static async Task SeedDatabaseAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;

            try
            {
                var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
                var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

                string adminRole = "Admin";
                string userRole = "User";

                if (!await roleManager.RoleExistsAsync(adminRole)) await roleManager.CreateAsync(new IdentityRole(adminRole));
                if (!await roleManager.RoleExistsAsync(userRole)) await roleManager.CreateAsync(new IdentityRole(userRole));

                string jsonFilePath = Path.Combine(AppContext.BaseDirectory, "admins.json");
                if (File.Exists(jsonFilePath))
                {
                    string json = await File.ReadAllTextAsync(jsonFilePath);
                    var adminsToSeed = JsonSerializer.Deserialize<List<AdminUser>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (adminsToSeed != null)
                    {
                        foreach (var adminData in adminsToSeed)
                        {
                            var adminUser = await userManager.FindByNameAsync(adminData.Username);
                            if (adminUser == null)
                            {
                                adminUser = new IdentityUser { UserName = adminData.Username, Email = adminData.Email, EmailConfirmed = true };
                                var createResult = await userManager.CreateAsync(adminUser, adminData.Password);
                                if (createResult.Succeeded) await userManager.AddToRoleAsync(adminUser, adminRole);
                            }
                        }
                    }
                }

                var context = services.GetRequiredService<Dsw2025TpiContext>();
                context.Seedwork<Customer>("customers.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error durante la carga de semilla: {ex.Message}");
            }
        }
    }
}
