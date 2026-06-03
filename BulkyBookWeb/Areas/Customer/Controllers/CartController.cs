using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.DataAccess;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;

namespace BulkyBookWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Authorize]
    public class CartController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<CartController> _logger;
        private readonly ApplicationDbContext _db;
        [BindProperty]
        public ShoppingCartVM ShoppingCartVM { get; set; } = new();

        public CartController(IUnitOfWork unitOfWork, IEmailSender emailSender, IConfiguration configuration, IWebHostEnvironment environment, ILogger<CartController> logger, ApplicationDbContext db)
        {
            _unitOfWork = unitOfWork;
            _emailSender = emailSender;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
            _db = db;
        }
        public IActionResult Index()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Challenge();
            }

            ShoppingCartVM = new ShoppingCartVM()
            {
                ListCart = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "Product"),
                OrderHeader = new()
            };
            foreach (var cart in ShoppingCartVM.ListCart)
            {
                cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price,
                    cart.Product.Price50, cart.Product.Price100);
                ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
            }
            return View(ShoppingCartVM);
        }

        public IActionResult Summary()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Challenge();
            }

            ShoppingCartVM = new ShoppingCartVM()
            {
                ListCart = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "Product"),
                OrderHeader = new()
            };
            if (!ShoppingCartVM.ListCart.Any())
            {
                TempData["info"] = "Your cart is empty.";
                return RedirectToAction(nameof(Index));
            }

            ShoppingCartVM.OrderHeader.ApplicationUser = _unitOfWork.ApplicationUser.GetFirstOrDefault(
                u => u.Id == userId);

            if (ShoppingCartVM.OrderHeader.ApplicationUser == null)
            {
                return Challenge();
            }

            if (PopulateShippingDetailsFromProfile(ShoppingCartVM.OrderHeader.ApplicationUser))
            {
                TempData["warning"] = "Please review and complete your shipping details before placing the order.";
            }



            foreach (var cart in ShoppingCartVM.ListCart)
            {
                cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price,
                    cart.Product.Price50, cart.Product.Price100);
                ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
            }
            return View(ShoppingCartVM);
        }

        [HttpPost]
        [ActionName("Summary")]
        [ValidateAntiForgeryToken]
        public IActionResult SummaryPOST()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Challenge();
            }

            ShoppingCartVM.ListCart = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "Product").ToList();

            if (!ShoppingCartVM.ListCart.Any())
            {
                TempData["error"] = "Your cart is empty.";
                return RedirectToAction(nameof(Index));
            }

            ApplicationUser applicationUser = _unitOfWork.ApplicationUser.GetFirstOrDefault(u => u.Id == userId);
            if (applicationUser == null)
            {
                return Challenge();
            }

            ShoppingCartVM.OrderHeader.OrderDate = DateTime.Now;
            ShoppingCartVM.OrderHeader.ApplicationUserId = userId;
            ShoppingCartVM.OrderHeader.OrderTotal = 0;

            foreach (var cart in ShoppingCartVM.ListCart)
            {
                cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price,
                    cart.Product.Price50, cart.Product.Price100);
                ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
            }

            NormalizeSubmittedShippingFields();
            RemoveServerControlledCheckoutModelState();
            RequireShippingFields();
            RejectPlaceholderShippingFields();

            if (!ModelState.IsValid)
            {
                ShoppingCartVM.OrderHeader.ApplicationUser = applicationUser;
                return View(ShoppingCartVM);
            }

            foreach (var cart in ShoppingCartVM.ListCart)
            {
                if (!cart.Product.IsActive || cart.Product.StockQuantity <= 0)
                {
                    TempData["error"] = $"'{cart.Product.Title}' is no longer available.";
                    return RedirectToAction(nameof(Index));
                }

                if (cart.Count > cart.Product.StockQuantity)
                {
                    TempData["error"] = $"Only {cart.Product.StockQuantity} copies of '{cart.Product.Title}' are available.";
                    return RedirectToAction(nameof(Index));
                }
            }

            if (applicationUser.CompanyId.GetValueOrDefault() == 0)
            {

                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusPending;
            }
            else
            {

                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusDelayedPayment;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusApproved;
                ShoppingCartVM.OrderHeader.PaymentDueDate = DateTime.Now.AddDays(30);
            }

            if (applicationUser.CompanyId.GetValueOrDefault() == 0 && !HasStripeApiKey() && !UseLocalStripeFallback())
            {
                _logger.LogWarning("Checkout blocked because Stripe is not configured for user {UserId}.", userId);
                TempData["error"] = "Payment processing is temporarily unavailable. Please try again later or contact support.";
                return RedirectToAction(nameof(Summary));
            }

            try
            {
                using var transaction = _db.Database.BeginTransaction();

                UpdateUserShippingProfile(applicationUser, ShoppingCartVM.OrderHeader);
                _unitOfWork.OrderHeader.Add(ShoppingCartVM.OrderHeader);
                _unitOfWork.Save();

                foreach (var cart in ShoppingCartVM.ListCart)
                {
                    cart.Product.StockQuantity -= cart.Count;
                    OrderDetail orderDetail = new()
                    {
                        ProductId = cart.ProductId,
                        OrderId = ShoppingCartVM.OrderHeader.Id,
                        Price = cart.Price,
                        Count = cart.Count
                    };
                    _unitOfWork.OrderDetail.Add(orderDetail);
                }

                _unitOfWork.Save();

                if (applicationUser.CompanyId.GetValueOrDefault() == 0)
                {
                    if (UseLocalStripeFallback())
                    {
                        ApplyDemoCheckoutFallback(userId, ShoppingCartVM.OrderHeader.Id);
                        transaction.Commit();

                        TempData["success"] = "Order placed successfully.";
                        return RedirectToAction("OrderConfirmation", "Cart", new { id = ShoppingCartVM.OrderHeader.Id });
                    }

                    //stripe settings
                    var domain = GetApplicationBaseUrl();
                    var options = new SessionCreateOptions
                    {
                        PaymentMethodTypes = new List<string>
                        {
                            "card",
                        },
                        LineItems = new List<SessionLineItemOptions>(),
                        Mode = "payment",
                        SuccessUrl = domain + $"customer/cart/OrderConfirmation?id={ShoppingCartVM.OrderHeader.Id}",
                        CancelUrl = domain + $"customer/cart/index",
                    };

                    foreach (var item in ShoppingCartVM.ListCart)
                    {
                        var sessionLineItem = new SessionLineItemOptions
                        {
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                UnitAmount = (long)(item.Price * 100),//20.00 ->2000
                                Currency = "usd",
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = item.Product.Title
                                },

                            },
                            Quantity = item.Count,
                        };
                        options.LineItems.Add(sessionLineItem);
                    }

                    var service = new SessionService();
                    Session session;
                    try
                    {
                        session = service.Create(options);
                    }
                    catch (Stripe.StripeException ex) when (IsLocalCheckoutFallbackEnabled())
                    {
                        _logger.LogWarning(ex, "Stripe checkout session creation failed for order {OrderId}. Using explicit demo/staging checkout fallback.", ShoppingCartVM.OrderHeader.Id);
                        ApplyDemoCheckoutFallback(userId, ShoppingCartVM.OrderHeader.Id);
                        transaction.Commit();

                        TempData["success"] = "Order placed successfully.";
                        return RedirectToAction("OrderConfirmation", "Cart", new { id = ShoppingCartVM.OrderHeader.Id });
                    }

                    _unitOfWork.OrderHeader.UpdateStripePaymentID(ShoppingCartVM.OrderHeader.Id, session.Id ?? string.Empty, session.PaymentIntentId ?? string.Empty);
                    _unitOfWork.Save();
                    transaction.Commit();

                    Response.Headers.Location = session.Url;
                    return new StatusCodeResult(303);
                }
                else
                {
                    transaction.Commit();
                    return RedirectToAction("OrderConfirmation", "Cart", new { id = ShoppingCartVM.OrderHeader.Id });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Checkout failed for user {UserId}.", userId);
                TempData["error"] = "Checkout could not be completed. Please review your cart and try again.";
                return RedirectToAction(nameof(Summary));
            }
        }

        public IActionResult OrderConfirmation(int id)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == id, includeProperties: "ApplicationUser");
            if (orderHeader == null)
            {
                return NotFound();
            }

            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Challenge();
            }

            if (orderHeader.ApplicationUserId != userId && !User.IsInRole(SD.Role_Admin) && !User.IsInRole(SD.Role_Employee))
            {
                _logger.LogWarning("Unauthorized order confirmation access attempt. User {UserId}, Order {OrderId}.", userId, id);
                return Forbid();
            }

            if (orderHeader.PaymentStatus != SD.PaymentStatusDelayedPayment && orderHeader.PaymentStatus != SD.PaymentStatusApproved)
            {
                if (!HasStripeApiKey() || string.IsNullOrWhiteSpace(orderHeader.SessionId))
                {
                    TempData["error"] = "Payment verification is temporarily unavailable. Please contact support if this order does not update automatically.";
                    return RedirectToAction(nameof(Index));
                }

                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);
                //checkthe stripe status
                if (session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderHeader.UpdateStripePaymentID(id, orderHeader.SessionId ?? string.Empty, session.PaymentIntentId ?? string.Empty);
                    _unitOfWork.OrderHeader.UpdateStatus(id, SD.StatusApproved, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
                }
            }
            if (!string.IsNullOrWhiteSpace(orderHeader.ApplicationUser.Email))
            {
                _emailSender.SendEmailAsync(orderHeader.ApplicationUser.Email, "New Order - Bulky Book", "<p>New Order Created</p>");
            }
            if (orderHeader.ApplicationUserId == userId)
            {
                List<ShoppingCart> shoppingCarts = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId ==
                orderHeader.ApplicationUserId).ToList();
                _unitOfWork.ShoppingCart.RemoveRange(shoppingCarts);
                _unitOfWork.Save();
                HttpContext.Session.SetInt32(SD.SessionCart, 0);
            }
            return View(id);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Plus(int cartId)
        {
            var cart = GetOwnedCartItem(cartId, includeProduct: true);
            if (cart == null)
            {
                return CartAccessDenied(cartId);
            }

            if (!cart.Product.IsActive || cart.Count >= cart.Product.StockQuantity)
            {
                TempData["error"] = $"Only {cart.Product.StockQuantity} copies of '{cart.Product.Title}' are available.";
                return RedirectToAction(nameof(Index));
            }

            _unitOfWork.ShoppingCart.IncrementCount(cart, 1);
            _unitOfWork.Save();
            SyncCartSession(cart.ApplicationUserId);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Minus(int cartId)
        {
            var cart = GetOwnedCartItem(cartId);
            if (cart == null)
            {
                return CartAccessDenied(cartId);
            }

            if (cart.Count <= 1)
            {
                _unitOfWork.ShoppingCart.Remove(cart);
            }
            else
            {
                _unitOfWork.ShoppingCart.DecrementCount(cart, 1);
            }
            _unitOfWork.Save();
            SyncCartSession(cart.ApplicationUserId);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Remove(int cartId)
        {
            var cart = GetOwnedCartItem(cartId);
            if (cart == null)
            {
                return CartAccessDenied(cartId);
            }

            _unitOfWork.ShoppingCart.Remove(cart);
            _unitOfWork.Save();
            SyncCartSession(cart.ApplicationUserId);
            return RedirectToAction(nameof(Index));
        }

        private double GetPriceBasedOnQuantity(double quantity, double price, double price50, double price100)
        {
            if (quantity <= 50)
            {
                return price;
            }
            else
            {
                if (quantity <= 100)
                {
                    return price50;
                }
                return price100;
            }
        }

        private string GetApplicationBaseUrl()
        {
            var configuredBaseUrl = _configuration["Application:BaseUrl"];
            var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? $"{Request.Scheme}://{Request.Host}"
                : configuredBaseUrl;

            return baseUrl.TrimEnd('/') + "/";
        }

        private bool HasStripeApiKey()
        {
            var secretKey = _configuration["Stripe:SecretKey"]?.Trim();
            return !string.IsNullOrWhiteSpace(secretKey)
                && (secretKey.StartsWith("sk_test_", StringComparison.Ordinal) ||
                    secretKey.StartsWith("sk_live_", StringComparison.Ordinal));
        }

        private bool IsLocalCheckoutFallbackEnabled()
        {
            return _configuration.GetValue<bool>("Stripe:EnableLocalCheckoutFallback");
        }

        private bool UseLocalStripeFallback()
        {
            return IsLocalCheckoutFallbackEnabled() && !HasStripeApiKey();
        }

        private static string BuildLocalStripeId(string prefix, int orderId)
        {
            return $"{prefix}_{orderId}";
        }

        private void ApplyDemoCheckoutFallback(string userId, int orderId)
        {
            // Demo/staging-only no-payment fallback. This path is enabled by explicit configuration
            // and must not be treated as production payment confirmation.
            _logger.LogInformation("Using explicit demo/staging checkout fallback for order {OrderId}.", orderId);
            _unitOfWork.OrderHeader.UpdateStripePaymentID(
                orderId,
                BuildLocalStripeId("local_demo_session", orderId),
                BuildLocalStripeId("local_demo_pi", orderId));
            _unitOfWork.OrderHeader.UpdateStatus(orderId, SD.StatusApproved, SD.PaymentStatusApproved);
            ClearCartForUser(userId);
            _unitOfWork.Save();
            HttpContext.Session.SetInt32(SD.SessionCart, 0);
        }

        private void ClearCartForUser(string userId)
        {
            var shoppingCarts = _unitOfWork.ShoppingCart
                .GetAll(u => u.ApplicationUserId == userId)
                .ToList();

            if (shoppingCarts.Any())
            {
                _unitOfWork.ShoppingCart.RemoveRange(shoppingCarts);
            }
        }

        private void SyncCartSession(string userId)
        {
            var cartQuantity = _unitOfWork.ShoppingCart
                .GetAll(u => u.ApplicationUserId == userId)
                .Sum(cart => cart.Count);

            HttpContext.Session.SetInt32(SD.SessionCart, cartQuantity);
        }

        private bool PopulateShippingDetailsFromProfile(ApplicationUser applicationUser)
        {
            var removedPlaceholder = false;
            ShoppingCartVM.OrderHeader.Name = GetProfileValue(applicationUser.Name, CheckoutProfileField.Name, ref removedPlaceholder);
            ShoppingCartVM.OrderHeader.PhoneNumber = GetProfileValue(applicationUser.PhoneNumber, CheckoutProfileField.PhoneNumber, ref removedPlaceholder);
            ShoppingCartVM.OrderHeader.StreetAddress = GetProfileValue(applicationUser.StreetAddress, CheckoutProfileField.StreetAddress, ref removedPlaceholder);
            ShoppingCartVM.OrderHeader.City = GetProfileValue(applicationUser.City, CheckoutProfileField.City, ref removedPlaceholder);
            ShoppingCartVM.OrderHeader.State = GetProfileValue(applicationUser.State, CheckoutProfileField.State, ref removedPlaceholder);
            ShoppingCartVM.OrderHeader.PostalCode = GetProfileValue(applicationUser.PostalCode, CheckoutProfileField.PostalCode, ref removedPlaceholder);
            return removedPlaceholder;
        }

        private static string GetProfileValue(string? value, CheckoutProfileField field, ref bool removedPlaceholder)
        {
            var trimmedValue = value?.Trim() ?? string.Empty;
            if (IsLocalPlaceholderValue(trimmedValue, field))
            {
                removedPlaceholder = true;
                return string.Empty;
            }

            return trimmedValue;
        }

        private void NormalizeSubmittedShippingFields()
        {
            ShoppingCartVM.OrderHeader.Name = ShoppingCartVM.OrderHeader.Name?.Trim() ?? string.Empty;
            ShoppingCartVM.OrderHeader.PhoneNumber = ShoppingCartVM.OrderHeader.PhoneNumber?.Trim() ?? string.Empty;
            ShoppingCartVM.OrderHeader.StreetAddress = ShoppingCartVM.OrderHeader.StreetAddress?.Trim() ?? string.Empty;
            ShoppingCartVM.OrderHeader.City = ShoppingCartVM.OrderHeader.City?.Trim() ?? string.Empty;
            ShoppingCartVM.OrderHeader.State = ShoppingCartVM.OrderHeader.State?.Trim() ?? string.Empty;
            ShoppingCartVM.OrderHeader.PostalCode = ShoppingCartVM.OrderHeader.PostalCode?.Trim() ?? string.Empty;
        }

        private void RemoveServerControlledCheckoutModelState()
        {
            string[] serverControlledFields =
            {
                "OrderHeader.ApplicationUser",
                "OrderHeader.ApplicationUserId",
                "OrderHeader.OrderDate",
                "OrderHeader.OrderTotal",
                "OrderHeader.OrderStatus",
                "OrderHeader.PaymentStatus",
                "OrderHeader.PaymentDate",
                "OrderHeader.PaymentDueDate",
                "OrderHeader.SessionId",
                "OrderHeader.PaymentIntentId",
                "OrderHeader.ShippingDate",
                "OrderHeader.Carrier",
                "OrderHeader.TrackingNumber",
                "ListCart"
            };

            foreach (var field in serverControlledFields)
            {
                ModelState.Remove(field);
                ModelState.Remove($"{nameof(ShoppingCartVM)}.{field}");
            }
        }

        private void RejectPlaceholderShippingFields()
        {
            AddPlaceholderModelError(CheckoutProfileField.Name, ShoppingCartVM.OrderHeader.Name, "OrderHeader.Name", "Please enter a real recipient name.");
            AddPlaceholderModelError(CheckoutProfileField.PhoneNumber, ShoppingCartVM.OrderHeader.PhoneNumber, "OrderHeader.PhoneNumber", "Please enter a real phone number.");
            AddPlaceholderModelError(CheckoutProfileField.StreetAddress, ShoppingCartVM.OrderHeader.StreetAddress, "OrderHeader.StreetAddress", "Please enter a real street address.");
            AddPlaceholderModelError(CheckoutProfileField.City, ShoppingCartVM.OrderHeader.City, "OrderHeader.City", "Please enter a real city.");
            AddPlaceholderModelError(CheckoutProfileField.State, ShoppingCartVM.OrderHeader.State, "OrderHeader.State", "Please enter a real state.");
            AddPlaceholderModelError(CheckoutProfileField.PostalCode, ShoppingCartVM.OrderHeader.PostalCode, "OrderHeader.PostalCode", "Please enter a real postal code.");
        }

        private void RequireShippingFields()
        {
            AddRequiredModelError(ShoppingCartVM.OrderHeader.Name, "OrderHeader.Name", "Name is required.");
            AddRequiredModelError(ShoppingCartVM.OrderHeader.PhoneNumber, "OrderHeader.PhoneNumber", "Phone number is required.");
            AddRequiredModelError(ShoppingCartVM.OrderHeader.StreetAddress, "OrderHeader.StreetAddress", "Street address is required.");
            AddRequiredModelError(ShoppingCartVM.OrderHeader.City, "OrderHeader.City", "City is required.");
            AddRequiredModelError(ShoppingCartVM.OrderHeader.State, "OrderHeader.State", "State is required.");
            AddRequiredModelError(ShoppingCartVM.OrderHeader.PostalCode, "OrderHeader.PostalCode", "Postal code is required.");
        }

        private void AddRequiredModelError(string value, string modelStateKey, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                AddModelErrorIfAbsent(modelStateKey, message);
            }
        }

        private void AddPlaceholderModelError(CheckoutProfileField field, string value, string modelStateKey, string message)
        {
            if (IsLocalPlaceholderValue(value, field))
            {
                AddModelErrorIfAbsent(modelStateKey, message);
            }
        }

        private void AddModelErrorIfAbsent(string modelStateKey, string message)
        {
            if (ModelState.TryGetValue(modelStateKey, out var entry) &&
                entry.Errors.Any(error => error.ErrorMessage == message))
            {
                return;
            }

            ModelState.AddModelError(modelStateKey, message);
        }

        private void UpdateUserShippingProfile(ApplicationUser applicationUser, OrderHeader orderHeader)
        {
            applicationUser.Name = orderHeader.Name;
            applicationUser.PhoneNumber = orderHeader.PhoneNumber;
            applicationUser.StreetAddress = orderHeader.StreetAddress;
            applicationUser.City = orderHeader.City;
            applicationUser.State = orderHeader.State;
            applicationUser.PostalCode = orderHeader.PostalCode;
        }

        private static bool IsLocalPlaceholderValue(string value, CheckoutProfileField field)
        {
            return field switch
            {
                CheckoutProfileField.Name => value.Equals("Local Admin", StringComparison.OrdinalIgnoreCase),
                CheckoutProfileField.PhoneNumber => value.Equals("0000000000", StringComparison.OrdinalIgnoreCase),
                CheckoutProfileField.StreetAddress => value.Equals("Local Street", StringComparison.OrdinalIgnoreCase),
                CheckoutProfileField.City => value.Equals("Local", StringComparison.OrdinalIgnoreCase),
                CheckoutProfileField.State => value.Equals("Local", StringComparison.OrdinalIgnoreCase),
                CheckoutProfileField.PostalCode => value.Equals("00000", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private string? GetCurrentUserId()
        {
            var claimsIdentity = User.Identity as ClaimsIdentity;
            return claimsIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private ShoppingCart? GetOwnedCartItem(int cartId, bool includeProduct = false)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return null;
            }

            return _unitOfWork.ShoppingCart.GetFirstOrDefault(
                u => u.Id == cartId && u.ApplicationUserId == userId,
                includeProperties: includeProduct ? "Product" : null);
        }

        private IActionResult CartAccessDenied(int cartId)
        {
            var userId = GetCurrentUserId();
            var cartExists = _unitOfWork.ShoppingCart.GetFirstOrDefault(u => u.Id == cartId, tracked: false) != null;
            if (cartExists)
            {
                _logger.LogWarning("Unauthorized cart mutation attempt. User {UserId}, Cart {CartId}.", userId, cartId);
                return Forbid();
            }

            return NotFound();
        }

        private enum CheckoutProfileField
        {
            Name,
            PhoneNumber,
            StreetAddress,
            City,
            State,
            PostalCode
        }
    }
}
