using BulkyBook.Models;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BulkyBook.DataAccess.DbInitializer
{
    public class DbInitializer : IDbInitializer
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DbInitializer> _logger;

        public DbInitializer(
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext db,
            IConfiguration configuration,
            ILogger<DbInitializer> logger)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _db = db;
            _configuration = configuration;
            _logger = logger;
        }

        public void Initialize()
        {
            //migrations if they are not applied
            try
            {
                if (_db.Database.GetPendingMigrations().Count() > 0)
                {
                    _db.Database.Migrate();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database migration failed during startup.");
                throw;
            }

            // create roles if they are not created
            string[] roles =
            {
                SD.Role_Admin,
                SD.Role_Employee,
                SD.Role_User_Indi,
                SD.Role_User_Comp
            };

            foreach (var role in roles)
            {
                if (!_roleManager.RoleExistsAsync(role).GetAwaiter().GetResult())
                {
                    _roleManager.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();
                }
            }

            var adminEmail = _configuration["SeedAdmin:Email"];
            var adminPassword = _configuration["SeedAdmin:Password"];

            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                _logger.LogWarning("Seed admin credentials are not configured. Skipping admin user seed.");
                return;
            }

            if (_db.ApplicationUsers.FirstOrDefault(u => u.Email == adminEmail) == null)
            {
                var adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    Name = _configuration["SeedAdmin:Name"] ?? "Local Admin",
                    PhoneNumber = _configuration["SeedAdmin:PhoneNumber"] ?? "0000000000",
                    StreetAddress = _configuration["SeedAdmin:StreetAddress"] ?? "Local",
                    State = _configuration["SeedAdmin:State"] ?? "Local",
                    PostalCode = _configuration["SeedAdmin:PostalCode"] ?? "00000",
                    City = _configuration["SeedAdmin:City"] ?? "Local"
                };

                var result = _userManager.CreateAsync(adminUser, adminPassword).GetAwaiter().GetResult();
                if (!result.Succeeded)
                {
                    var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                    _logger.LogError("Failed to create seed admin user {Email}: {Errors}", adminEmail, errors);
                    throw new InvalidOperationException($"Failed to create seed admin user: {errors}");
                }
            }

            var user = _db.ApplicationUsers.FirstOrDefault(u => u.Email == adminEmail);
            if (user != null && !_userManager.IsInRoleAsync(user, SD.Role_Admin).GetAwaiter().GetResult())
            {
                _userManager.AddToRoleAsync(user, SD.Role_Admin).GetAwaiter().GetResult();
            }

            return;
        }
    }
}
