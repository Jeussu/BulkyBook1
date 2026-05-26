using BulkyBook.Models;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BulkyBook.DataAccess.DbInitializer
{
    public interface IDemoDataSeeder
    {
        void Seed();
    }

    public class DemoDataSeeder : IDemoDataSeeder
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<DemoDataSeeder> _logger;

        public DemoDataSeeder(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ILogger<DemoDataSeeder> logger)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _logger = logger;
        }

        public void Seed()
        {
            EnsureRoles();
            var companies = EnsureCompanies();
            var categories = EnsureCategories();
            var coverTypes = EnsureCoverTypes();
            var users = EnsureUsers(companies);
            var products = EnsureProducts(categories, coverTypes);

            EnsureShoppingCarts(users, products);
            EnsureOrders(users, products);
        }

        private void EnsureRoles()
        {
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
        }

        private Dictionary<string, Company> EnsureCompanies()
        {
            var companies = new[]
            {
                new Company { Name = "Acme Books Ltd.", StreetAddress = "12 Nguyen Hue Street", City = "Ho Chi Minh City", State = "HCMC", PostalCode = "700000", PhoneNumber = "028-5555-0101" },
                new Company { Name = "TechSoft Vietnam", StreetAddress = "88 Tran Duy Hung", City = "Ha Noi", State = "HN", PostalCode = "100000", PhoneNumber = "024-5555-0202" },
                new Company { Name = "Contoso Education", StreetAddress = "42 Bach Dang", City = "Da Nang", State = "DN", PostalCode = "550000", PhoneNumber = "0236-5555-0303" },
                new Company { Name = "Northwind Publishing", StreetAddress = "15 Le Loi", City = "Can Tho", State = "CT", PostalCode = "900000", PhoneNumber = "0292-5555-0404" }
            };

            foreach (var company in companies)
            {
                if (!_db.Companies.Any(c => c.Name == company.Name))
                {
                    _db.Companies.Add(company);
                }
            }

            _db.SaveChanges();
            return _db.Companies
                .Where(c => companies.Select(x => x.Name).Contains(c.Name))
                .ToDictionary(c => c.Name);
        }

        private Dictionary<string, Category> EnsureCategories()
        {
            var categories = new[]
            {
                new Category { Name = "Programming", DisplayOrder = 1 },
                new Category { Name = "Web Development", DisplayOrder = 2 },
                new Category { Name = "Database", DisplayOrder = 3 },
                new Category { Name = "Cloud", DisplayOrder = 4 },
                new Category { Name = "DevOps", DisplayOrder = 5 },
                new Category { Name = "Software Architecture", DisplayOrder = 6 },
                new Category { Name = "Business", DisplayOrder = 7 },
                new Category { Name = "Fiction", DisplayOrder = 8 }
            };

            foreach (var category in categories)
            {
                if (!_db.Categories.Any(c => c.Name == category.Name))
                {
                    _db.Categories.Add(category);
                }
            }

            _db.SaveChanges();
            return _db.Categories
                .Where(c => categories.Select(x => x.Name).Contains(c.Name))
                .ToDictionary(c => c.Name);
        }

        private Dictionary<string, CoverType> EnsureCoverTypes()
        {
            string[] coverTypeNames =
            {
                "Paperback",
                "Hardcover",
                "Kindle",
                "Audio Book",
                "Spiral Bound"
            };

            foreach (var coverTypeName in coverTypeNames)
            {
                if (!_db.CoverTypes.Any(c => c.Name == coverTypeName))
                {
                    _db.CoverTypes.Add(new CoverType { Name = coverTypeName });
                }
            }

            _db.SaveChanges();
            return _db.CoverTypes
                .Where(c => coverTypeNames.Contains(c.Name))
                .ToDictionary(c => c.Name);
        }

        private Dictionary<string, ApplicationUser> EnsureUsers(Dictionary<string, Company> companies)
        {
            var users = new[]
            {
                new DemoUser("admin@bulky.local", "Admin123!", SD.Role_Admin, "Local Admin", "090-000-0001", "1 Admin Way", "Ho Chi Minh City", "HCMC", "700000"),
                new DemoUser("employee@bulky.local", "Employee123!", SD.Role_Employee, "Evelyn Employee", "090-000-0002", "2 Staff Avenue", "Ho Chi Minh City", "HCMC", "700000"),
                new DemoUser("customer@bulky.local", "Customer123!", SD.Role_User_Indi, "Chris Customer", "090-000-0003", "3 Customer Street", "Da Nang", "DN", "550000"),
                new DemoUser("customer2@bulky.local", "Customer123!", SD.Role_User_Indi, "Casey Reader", "090-000-0004", "4 Reader Road", "Ha Noi", "HN", "100000"),
                new DemoUser("company@bulky.local", "Company123!", SD.Role_User_Comp, "Morgan Company", "090-000-0005", "12 Nguyen Hue Street", "Ho Chi Minh City", "HCMC", "700000", "Acme Books Ltd.")
            };

            foreach (var user in users)
            {
                EnsureUser(user, companies);
            }

            return _db.ApplicationUsers
                .Where(u => users.Select(x => x.Email).Contains(u.Email!))
                .ToDictionary(u => u.Email!);
        }

        private void EnsureUser(DemoUser user, Dictionary<string, Company> companies)
        {
            var identityUser = _userManager.FindByEmailAsync(user.Email).GetAwaiter().GetResult();
            var companyId = user.CompanyName != null ? companies[user.CompanyName].Id : (int?)null;

            if (identityUser == null)
            {
                var applicationUser = new ApplicationUser
                {
                    UserName = user.Email,
                    Email = user.Email,
                    EmailConfirmed = true,
                    Name = user.Name,
                    PhoneNumber = user.PhoneNumber,
                    StreetAddress = user.StreetAddress,
                    City = user.City,
                    State = user.State,
                    PostalCode = user.PostalCode,
                    CompanyId = companyId
                };

                var createResult = _userManager.CreateAsync(applicationUser, user.Password).GetAwaiter().GetResult();
                if (!createResult.Succeeded)
                {
                    var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                    _logger.LogError("Failed to create demo user {Email}: {Errors}", user.Email, errors);
                    throw new InvalidOperationException($"Failed to create demo user {user.Email}: {errors}");
                }

                identityUser = applicationUser;
            }
            else
            {
                var applicationUser = _db.ApplicationUsers.FirstOrDefault(u => u.Id == identityUser.Id);
                if (applicationUser == null)
                {
                    _logger.LogWarning("Demo user {Email} exists but is not an ApplicationUser. Skipping profile update.", user.Email);
                    return;
                }

                var changed = false;
                changed |= SetIfMissing(value => applicationUser.Name = value, applicationUser.Name, user.Name);
                changed |= SetIfMissing(value => applicationUser.PhoneNumber = value, applicationUser.PhoneNumber, user.PhoneNumber);
                changed |= SetIfMissing(value => applicationUser.StreetAddress = value, applicationUser.StreetAddress, user.StreetAddress);
                changed |= SetIfMissing(value => applicationUser.City = value, applicationUser.City, user.City);
                changed |= SetIfMissing(value => applicationUser.State = value, applicationUser.State, user.State);
                changed |= SetIfMissing(value => applicationUser.PostalCode = value, applicationUser.PostalCode, user.PostalCode);

                if (!applicationUser.EmailConfirmed)
                {
                    applicationUser.EmailConfirmed = true;
                    changed = true;
                }

                if (companyId.HasValue && applicationUser.CompanyId != companyId.Value)
                {
                    applicationUser.CompanyId = companyId.Value;
                    changed = true;
                }

                if (changed)
                {
                    _userManager.UpdateAsync(applicationUser).GetAwaiter().GetResult();
                }

                identityUser = applicationUser;
            }

            if (!_userManager.IsInRoleAsync(identityUser, user.Role).GetAwaiter().GetResult())
            {
                _userManager.AddToRoleAsync(identityUser, user.Role).GetAwaiter().GetResult();
            }
        }

        private static bool SetIfMissing(Action<string> setter, string? currentValue, string seedValue)
        {
            if (!string.IsNullOrWhiteSpace(currentValue))
            {
                return false;
            }

            setter(seedValue);
            return true;
        }

        private Dictionary<string, Product> EnsureProducts(
            Dictionary<string, Category> categories,
            Dictionary<string, CoverType> coverTypes)
        {
            var products = GetDemoProducts(categories, coverTypes);

            foreach (var product in products)
            {
                if (!_db.Products.Any(p => p.ISBN == product.ISBN))
                {
                    _db.Products.Add(product);
                }
            }

            _db.SaveChanges();
            return _db.Products
                .Where(p => products.Select(x => x.ISBN).Contains(p.ISBN))
                .ToDictionary(p => p.ISBN);
        }

        private static Product[] GetDemoProducts(
            Dictionary<string, Category> categories,
            Dictionary<string, CoverType> coverTypes)
        {
            var images = new[]
            {
                "02acb298-f940-4a95-a064-3401c12ecb72.jpg",
                "0a78204b-6de5-4a88-9f3e-e5b6fa03ef25.jpg",
                "0ba168a8-6817-432d-9db6-00970140a7eb.jpg",
                "2d3d80b1-d941-4495-8bcc-007732999b60.jpg",
                "2f9ab1b8-e343-4c1e-8aad-6fd063c886ff.jpg",
                "3928e4b0-c869-4fc4-83b5-fda66b959513.jpg",
                "472230df-1d33-43e5-947e-b5455b3d87f4.jpg",
                "4c6e2d10-f5ce-4e65-93ed-77b555e86b61.jpg",
                "53f7359f-df8a-4a08-967a-90a2bbd00d26.jpg",
                "6700b67c-8820-4191-b829-9c8046fc2dd8.jpg",
                "8ef513b7-4ae4-4181-8bfe-c12095b96147.jpg",
                "9c157586-efc3-4971-8d5c-bdd81f0d615f.jpg",
                "a11c47e0-e04b-4580-8a98-e4719ac2b2dd.jpg",
                "a2fd21f6-182f-4df4-ab98-5300f2d78bc2.jpg",
                "a8a86994-905e-43e5-ba5e-25131dbf8b8b.jpg",
                "bf7098ff-eb0e-4a04-8fff-f2e5e71dd2a9.jpg",
                "c6781543-56e7-4df5-8588-7c65dcfc9d21.jpg",
                "ca079f07-7040-4ccc-b6c5-a12f811f8948.jpg",
                "cb75a115-f10f-4934-801a-4ffd3222f9ec.jpg",
                "cee3a282-6d8b-4ca4-bf97-91c93f955373.jpg"
            };

            var specs = new[]
            {
                new ProductSpec("DEMO-978-000000001", "ASP.NET Core MVC Fundamentals", "Anika Patel", "Programming", "Paperback", 59, 49, 45, 39),
                new ProductSpec("DEMO-978-000000002", "Entity Framework Core in Action", "Marcus Lee", "Database", "Hardcover", 69, 57, 52, 47),
                new ProductSpec("DEMO-978-000000003", "SQL Server for Developers", "Diana Nguyen", "Database", "Paperback", 54, 44, 40, 36),
                new ProductSpec("DEMO-978-000000004", "Clean Architecture with .NET", "Owen Carter", "Software Architecture", "Hardcover", 79, 65, 59, 53),
                new ProductSpec("DEMO-978-000000005", "Practical C# 12", "Nora Tran", "Programming", "Kindle", 49, 39, 35, 31),
                new ProductSpec("DEMO-978-000000006", "JavaScript for Razor Developers", "Leo Martin", "Web Development", "Paperback", 52, 42, 38, 34),
                new ProductSpec("DEMO-978-000000007", "Azure App Service Deployment", "Priya Shah", "Cloud", "Paperback", 64, 52, 48, 43),
                new ProductSpec("DEMO-978-000000008", "Stripe Payments for .NET", "Henry Brooks", "Business", "Kindle", 46, 37, 33, 29),
                new ProductSpec("DEMO-978-000000009", "Identity and Security in ASP.NET Core", "Grace Kim", "Web Development", "Hardcover", 72, 60, 55, 50),
                new ProductSpec("DEMO-978-000000010", "Docker for .NET Developers", "Mateo Silva", "DevOps", "Paperback", 58, 47, 43, 38),
                new ProductSpec("DEMO-978-000000011", "Git and GitHub Workflow", "Lena Fischer", "DevOps", "Spiral Bound", 39, 31, 28, 25),
                new ProductSpec("DEMO-978-000000012", "System Design Notes", "Jon Bell", "Software Architecture", "Hardcover", 74, 61, 56, 51),
                new ProductSpec("DEMO-978-000000013", "Domain-Driven Design Basics", "Sofia Rossi", "Software Architecture", "Paperback", 63, 51, 47, 42),
                new ProductSpec("DEMO-978-000000014", "Refactoring Legacy Code", "Mina Park", "Programming", "Paperback", 56, 46, 41, 37),
                new ProductSpec("DEMO-978-000000015", "The Pragmatic Programmer Style Demo Book", "Alex Rivera", "Fiction", "Audio Book", 35, 28, 25, 22),
                new ProductSpec("DEMO-978-000000016", "E-Commerce Patterns", "Victor Chen", "Business", "Hardcover", 68, 55, 50, 45),
                new ProductSpec("DEMO-978-000000017", "Data Structures in C#", "Ivy Johnson", "Programming", "Paperback", 50, 40, 36, 32),
                new ProductSpec("DEMO-978-000000018", "Cloud Native .NET", "Sam Wilson", "Cloud", "Kindle", 66, 54, 49, 44),
                new ProductSpec("DEMO-978-000000019", "Microservices with ASP.NET Core", "Elena Garcia", "Cloud", "Hardcover", 82, 68, 62, 56),
                new ProductSpec("DEMO-978-000000020", "Debugging Production Apps", "Noah Brown", "DevOps", "Audio Book", 48, 39, 35, 31)
            };

            return specs.Select((spec, index) => new Product
            {
                ISBN = spec.ISBN,
                Title = spec.Title,
                Author = spec.Author,
                Description = $"<p>{spec.Title} is a local demo book for testing BulkyBook browsing, cart, checkout, and order workflows.</p>",
                ListPrice = spec.ListPrice,
                Price = spec.Price,
                Price50 = spec.Price50,
                Price100 = spec.Price100,
                CategoryId = categories[spec.CategoryName].Id,
                CoverTypeId = coverTypes[spec.CoverTypeName].Id,
                ImageUrl = $"/images/products/{images[index]}"
            }).ToArray();
        }

        private void EnsureShoppingCarts(
            Dictionary<string, ApplicationUser> users,
            Dictionary<string, Product> products)
        {
            var carts = new[]
            {
                new DemoCart("customer@bulky.local", "DEMO-978-000000001", 1),
                new DemoCart("customer@bulky.local", "DEMO-978-000000008", 3),
                new DemoCart("customer@bulky.local", "DEMO-978-000000014", 55),
                new DemoCart("company@bulky.local", "DEMO-978-000000004", 25),
                new DemoCart("company@bulky.local", "DEMO-978-000000019", 120)
            };

            foreach (var cart in carts)
            {
                var user = users[cart.Email];
                var product = products[cart.ISBN];

                if (!_db.ShoppingCarts.Any(c => c.ApplicationUserId == user.Id && c.ProductId == product.Id))
                {
                    _db.ShoppingCarts.Add(new ShoppingCart
                    {
                        ApplicationUserId = user.Id,
                        ProductId = product.Id,
                        Count = cart.Count
                    });
                }
            }

            _db.SaveChanges();
        }

        private void EnsureOrders(
            Dictionary<string, ApplicationUser> users,
            Dictionary<string, Product> products)
        {
            var today = DateTime.Today;
            var orders = new[]
            {
                new DemoOrder("demo_session_pending_001", null, "customer@bulky.local", SD.StatusPending, SD.PaymentStatusPending, today.AddDays(-1), null, null, null, null,
                    new[] { new DemoOrderLine("DEMO-978-000000001", 1), new DemoOrderLine("DEMO-978-000000006", 2) }),
                new DemoOrder("demo_session_paid_001", "demo_pi_paid_001", "customer@bulky.local", SD.StatusApproved, SD.PaymentStatusApproved, today.AddDays(-3), today.AddDays(-3).AddMinutes(15), null, null, null,
                    new[] { new DemoOrderLine("DEMO-978-000000002", 1), new DemoOrderLine("DEMO-978-000000008", 1) }),
                new DemoOrder("demo_session_processing_001", "demo_pi_paid_002", "customer@bulky.local", SD.StatusInProcess, SD.PaymentStatusApproved, today.AddDays(-5), today.AddDays(-5).AddMinutes(20), null, null, null,
                    new[] { new DemoOrderLine("DEMO-978-000000009", 2), new DemoOrderLine("DEMO-978-000000011", 1) }),
                new DemoOrder("demo_session_shipped_001", "demo_pi_paid_003", "customer@bulky.local", SD.StatusShipped, SD.PaymentStatusApproved, today.AddDays(-10), today.AddDays(-10).AddMinutes(10), today.AddDays(-7), "UPS", "DEMO-UPS-1001",
                    new[] { new DemoOrderLine("DEMO-978-000000004", 1), new DemoOrderLine("DEMO-978-000000017", 3) }),
                new DemoOrder("demo_session_cancelled_001", null, "customer2@bulky.local", SD.StatusCancelled, SD.StatusCancelled, today.AddDays(-12), null, null, null, null,
                    new[] { new DemoOrderLine("DEMO-978-000000015", 1), new DemoOrderLine("DEMO-978-000000003", 1) }),
                new DemoOrder("demo_session_company_delayed_001", null, "company@bulky.local", SD.StatusApproved, SD.PaymentStatusDelayedPayment, today.AddDays(-2), null, null, null, null,
                    new[] { new DemoOrderLine("DEMO-978-000000010", 60), new DemoOrderLine("DEMO-978-000000012", 10) }),
                new DemoOrder("demo_session_company_processing_001", null, "company@bulky.local", SD.StatusInProcess, SD.PaymentStatusDelayedPayment, today.AddDays(-6), null, null, null, null,
                    new[] { new DemoOrderLine("DEMO-978-000000013", 80), new DemoOrderLine("DEMO-978-000000016", 15) }),
                new DemoOrder("demo_session_company_shipped_001", null, "company@bulky.local", SD.StatusShipped, SD.PaymentStatusDelayedPayment, today.AddDays(-18), null, today.AddDays(-14), "FedEx", "DEMO-FDX-2001",
                    new[] { new DemoOrderLine("DEMO-978-000000018", 120), new DemoOrderLine("DEMO-978-000000020", 35) }),
                new DemoOrder("demo_session_company_overdue_001", null, "company@bulky.local", SD.StatusShipped, SD.PaymentStatusDelayedPayment, today.AddDays(-45), null, today.AddDays(-40), "DHL", "DEMO-DHL-3001",
                    new[] { new DemoOrderLine("DEMO-978-000000005", 150), new DemoOrderLine("DEMO-978-000000007", 45) }),
                new DemoOrder("demo_session_history_001", "demo_pi_paid_004", "customer2@bulky.local", SD.StatusShipped, SD.PaymentStatusApproved, today.AddDays(-30), today.AddDays(-30).AddMinutes(30), today.AddDays(-26), "VNPost", "DEMO-VNP-4001",
                    new[] { new DemoOrderLine("DEMO-978-000000014", 2), new DemoOrderLine("DEMO-978-000000019", 1) })
            };

            foreach (var order in orders)
            {
                if (_db.OrderHeaders.Any(o => o.SessionId == order.SessionId))
                {
                    continue;
                }

                var user = users[order.Email];
                var orderHeader = new OrderHeader
                {
                    ApplicationUserId = user.Id,
                    Name = user.Name,
                    PhoneNumber = user.PhoneNumber ?? "090-000-0000",
                    StreetAddress = user.StreetAddress ?? "Local Street",
                    City = user.City ?? "Local",
                    State = user.State ?? "Local",
                    PostalCode = user.PostalCode ?? "00000",
                    OrderDate = order.OrderDate,
                    ShippingDate = order.ShippingDate ?? DateTime.MinValue,
                    PaymentDate = order.PaymentDate ?? DateTime.MinValue,
                    PaymentDueDate = order.PaymentStatus == SD.PaymentStatusDelayedPayment
                        ? order.OrderDate.AddDays(30)
                        : DateTime.MinValue,
                    OrderStatus = order.OrderStatus,
                    PaymentStatus = order.PaymentStatus,
                    Carrier = order.Carrier,
                    TrackingNumber = order.TrackingNumber,
                    SessionId = order.SessionId,
                    PaymentIntentId = order.PaymentIntentId
                };

                _db.OrderHeaders.Add(orderHeader);
                _db.SaveChanges();

                var orderTotal = 0d;
                foreach (var line in order.Lines)
                {
                    var product = products[line.ISBN];
                    var price = GetPriceBasedOnQuantity(line.Count, product);
                    orderTotal += price * line.Count;

                    _db.OrderDetail.Add(new OrderDetail
                    {
                        OrderId = orderHeader.Id,
                        ProductId = product.Id,
                        Count = line.Count,
                        Price = price
                    });
                }

                orderHeader.OrderTotal = orderTotal;
                _db.OrderHeaders.Update(orderHeader);
                _db.SaveChanges();
            }
        }

        private static double GetPriceBasedOnQuantity(int quantity, Product product)
        {
            if (quantity <= 50)
            {
                return product.Price;
            }

            return quantity <= 100 ? product.Price50 : product.Price100;
        }

        private sealed record DemoUser(
            string Email,
            string Password,
            string Role,
            string Name,
            string PhoneNumber,
            string StreetAddress,
            string City,
            string State,
            string PostalCode,
            string? CompanyName = null);

        private sealed record ProductSpec(
            string ISBN,
            string Title,
            string Author,
            string CategoryName,
            string CoverTypeName,
            double ListPrice,
            double Price,
            double Price50,
            double Price100);

        private sealed record DemoCart(string Email, string ISBN, int Count);

        private sealed record DemoOrderLine(string ISBN, int Count);

        private sealed record DemoOrder(
            string SessionId,
            string? PaymentIntentId,
            string Email,
            string OrderStatus,
            string PaymentStatus,
            DateTime OrderDate,
            DateTime? PaymentDate,
            DateTime? ShippingDate,
            string? Carrier,
            string? TrackingNumber,
            DemoOrderLine[] Lines);
    }
}
