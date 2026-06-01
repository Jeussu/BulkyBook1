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
        //total price
        public int OrderTotal { get; set; }
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

            ShoppingCartVM.OrderHeader.Name = ShoppingCartVM.OrderHeader.ApplicationUser.Name ?? string.Empty;
            ShoppingCartVM.OrderHeader.PhoneNumber = ShoppingCartVM.OrderHeader.ApplicationUser.PhoneNumber ?? string.Empty;
            ShoppingCartVM.OrderHeader.StreetAddress = ShoppingCartVM.OrderHeader.ApplicationUser.StreetAddress ?? string.Empty;
            ShoppingCartVM.OrderHeader.City = ShoppingCartVM.OrderHeader.ApplicationUser.City ?? string.Empty;
            ShoppingCartVM.OrderHeader.State = ShoppingCartVM.OrderHeader.ApplicationUser.State ?? string.Empty;
            ShoppingCartVM.OrderHeader.PostalCode = ShoppingCartVM.OrderHeader.ApplicationUser.PostalCode ?? string.Empty;



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

            ShoppingCartVM.OrderHeader.OrderDate = System.DateTime.Now;
            ShoppingCartVM.OrderHeader.ApplicationUserId = userId;

            foreach (var cart in ShoppingCartVM.ListCart)
            {
                cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price,
                    cart.Product.Price50, cart.Product.Price100);
                ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
            }

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
                        _logger.LogInformation("Using local checkout fallback for order {OrderId}.", ShoppingCartVM.OrderHeader.Id);
                        _unitOfWork.OrderHeader.UpdateStripePaymentID(
                            ShoppingCartVM.OrderHeader.Id,
                            BuildLocalStripeId("local_demo_session", ShoppingCartVM.OrderHeader.Id),
                            BuildLocalStripeId("local_demo_pi", ShoppingCartVM.OrderHeader.Id));
                        _unitOfWork.OrderHeader.UpdateStatus(ShoppingCartVM.OrderHeader.Id, SD.StatusApproved, SD.PaymentStatusApproved);
                        _unitOfWork.Save();
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
                    Session session = service.Create(options);
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
                HttpContext.Session.Clear();
                _unitOfWork.ShoppingCart.RemoveRange(shoppingCarts);
                _unitOfWork.Save();
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
                var count = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == cart.ApplicationUserId).ToList().Count - 1;
                HttpContext.Session.SetInt32(SD.SessionCart, count);
            }
            else
            {
                _unitOfWork.ShoppingCart.DecrementCount(cart, 1);
            }
            _unitOfWork.Save();
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
            var count = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == cart.ApplicationUserId).ToList().Count;
            HttpContext.Session.SetInt32(SD.SessionCart, count);
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
            return !string.IsNullOrWhiteSpace(_configuration["Stripe:SecretKey"]);
        }

        private bool UseLocalStripeFallback()
        {
            return _environment.IsDevelopment()
                && _configuration.GetValue<bool>("Stripe:EnableLocalCheckoutFallback")
                && !HasStripeApiKey();
        }

        private static string BuildLocalStripeId(string prefix, int orderId)
        {
            return $"{prefix}_{orderId}";
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
    }
}
