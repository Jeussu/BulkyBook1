using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;

namespace BulkyBookWeb.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize]
    public class OrderController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<OrderController> _logger;
        [BindProperty]
        public OrderVm OrderVM { get; set; } = new();
        public OrderController(IUnitOfWork unitOfWork, IConfiguration configuration, IWebHostEnvironment environment, ILogger<OrderController> logger)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Details(int orderId)
        {
            var orderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == orderId, includeProperties: "ApplicationUser");
            if (orderHeader == null)
            {
                return NotFound();
            }

            if (!CanAccessOrder(orderHeader))
            {
                _logger.LogWarning("Unauthorized order details access attempt. User {UserId}, Order {OrderId}.", GetCurrentUserId(), orderId);
                return Forbid();
            }

            OrderVM = new OrderVm()
            {
                OrderHeader = orderHeader,
                OrderDetail = _unitOfWork.OrderDetail.GetAll(u => u.OrderId == orderId, includeProperties: "Product"),
            };
            return View(OrderVM);
        }

        [ActionName("Details")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Details_PAY_NOW()
        {
            OrderVM.OrderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, includeProperties: "ApplicationUser");
            if (OrderVM.OrderHeader == null)
            {
                return NotFound();
            }

            if (!CanAccessOrder(OrderVM.OrderHeader))
            {
                _logger.LogWarning("Unauthorized order pay-now attempt. User {UserId}, Order {OrderId}.", GetCurrentUserId(), OrderVM.OrderHeader.Id);
                return Forbid();
            }

            OrderVM.OrderDetail = _unitOfWork.OrderDetail.GetAll(u => u.OrderId == OrderVM.OrderHeader.Id, includeProperties: "Product");

            if (UseLocalStripeFallback())
            {
                _logger.LogInformation("Using local payment fallback for order {OrderId}.", OrderVM.OrderHeader.Id);
                _unitOfWork.OrderHeader.UpdateStripePaymentID(
                    OrderVM.OrderHeader.Id,
                    BuildLocalStripeId("local_demo_session", OrderVM.OrderHeader.Id),
                    BuildLocalStripeId("local_demo_pi", OrderVM.OrderHeader.Id));
                _unitOfWork.OrderHeader.UpdateStatus(OrderVM.OrderHeader.Id, OrderVM.OrderHeader.OrderStatus ?? SD.StatusShipped, SD.PaymentStatusApproved);
                _unitOfWork.Save();

                TempData["Success"] = "Payment recorded successfully.";
                return RedirectToAction("PaymentConfirmation", "Order", new { orderHeaderid = OrderVM.OrderHeader.Id });
            }

            if (!HasStripeApiKey())
            {
                _logger.LogWarning("Order payment blocked because Stripe is not configured. Order {OrderId}.", OrderVM.OrderHeader.Id);
                TempData["error"] = "Payment processing is temporarily unavailable. Please try again later or contact support.";
                return RedirectToAction("Details", "Order", new { orderId = OrderVM.OrderHeader.Id });
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
                SuccessUrl = domain + $"admin/order/PaymentConfirmation?orderHeaderid={OrderVM.OrderHeader.Id}",
                CancelUrl = domain + $"admin/order/details?orderId={OrderVM.OrderHeader.Id}",
            };

            foreach (var item in OrderVM.OrderDetail)
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
            _unitOfWork.OrderHeader.UpdateStripePaymentID(OrderVM.OrderHeader.Id, session.Id ?? string.Empty, session.PaymentIntentId ?? string.Empty);
            _unitOfWork.Save();

            Response.Headers.Location = session.Url;
            return new StatusCodeResult(303);        
        }

        public IActionResult PaymentConfirmation(int orderHeaderid)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == orderHeaderid);
            if (orderHeader == null)
            {
                return NotFound();
            }

            if (!CanAccessOrder(orderHeader))
            {
                _logger.LogWarning("Unauthorized payment confirmation access attempt. User {UserId}, Order {OrderId}.", GetCurrentUserId(), orderHeaderid);
                return Forbid();
            }

            if (orderHeader.PaymentStatus == SD.PaymentStatusDelayedPayment)
            {
                if (UseLocalStripeFallback())
                {
                    _unitOfWork.OrderHeader.UpdateStatus(orderHeaderid, orderHeader.OrderStatus ?? SD.StatusShipped, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
                    return View(orderHeaderid);
                }

                if (!HasStripeApiKey() || string.IsNullOrWhiteSpace(orderHeader.SessionId))
                {
                    TempData["error"] = "Payment verification is temporarily unavailable. Please review the order details or contact support.";
                    return RedirectToAction("Details", "Order", new { orderId = orderHeaderid });
                }

                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);
                //checkthe stripe status
                if (session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderHeader.UpdateStatus(orderHeaderid, orderHeader.OrderStatus ?? SD.StatusShipped, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
                }
            }
            return View(orderHeaderid);
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateOrderDetail()
        {
            var orderHEaderFromDb = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked: false);
            if (orderHEaderFromDb == null)
            {
                return NotFound();
            }

            orderHEaderFromDb.Name = OrderVM.OrderHeader.Name;
            orderHEaderFromDb.PhoneNumber = OrderVM.OrderHeader.PhoneNumber;
            orderHEaderFromDb.StreetAddress = OrderVM.OrderHeader.StreetAddress;
            orderHEaderFromDb.City = OrderVM.OrderHeader.City;
            orderHEaderFromDb.State = OrderVM.OrderHeader.State;
            orderHEaderFromDb.PostalCode = OrderVM.OrderHeader.PostalCode;
            if (OrderVM.OrderHeader.Carrier != null)
            {
                orderHEaderFromDb.Carrier = OrderVM.OrderHeader.Carrier;
            }
            if (OrderVM.OrderHeader.TrackingNumber != null)
            {
                orderHEaderFromDb.TrackingNumber = OrderVM.OrderHeader.TrackingNumber;
            }
            _unitOfWork.OrderHeader.Update(orderHEaderFromDb);
            _unitOfWork.Save();
            TempData["Success"] = "Order Details Updated Successfully.";
            return RedirectToAction("Details", "Order", new { orderId = orderHEaderFromDb.Id });
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        [ValidateAntiForgeryToken]
        public IActionResult StartProcessing()
        {
            if (_unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked: false) == null)
            {
                return NotFound();
            }

            _unitOfWork.OrderHeader.UpdateStatus(OrderVM.OrderHeader.Id, SD.StatusInProcess);
            _unitOfWork.Save();
            _logger.LogInformation("Order {OrderId} marked Processing by user {UserId}.", OrderVM.OrderHeader.Id, GetCurrentUserId());
            TempData["Success"] = "Order Status Updated Successfully.";
            return RedirectToAction("Details", "Order", new { orderId = OrderVM.OrderHeader.Id });
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        [ValidateAntiForgeryToken]
        public IActionResult ShipOrder()
        {
            var orderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked: false);
            if (orderHeader == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(OrderVM.OrderHeader.Carrier) ||
                string.IsNullOrWhiteSpace(OrderVM.OrderHeader.TrackingNumber))
            {
                TempData["error"] = "Carrier and tracking number are required before shipping an order.";
                return RedirectToAction("Details", "Order", new { orderId = OrderVM.OrderHeader.Id });
            }

            orderHeader.TrackingNumber = OrderVM.OrderHeader.TrackingNumber;
            orderHeader.Carrier = OrderVM.OrderHeader.Carrier;
            orderHeader.OrderStatus = SD.StatusShipped;
            orderHeader.ShippingDate = DateTime.Now;
            if (orderHeader.PaymentStatus == SD.PaymentStatusDelayedPayment)
            {
                orderHeader.PaymentDueDate = DateTime.Now.AddDays(30);
            }
            _unitOfWork.OrderHeader.Update(orderHeader);
            _unitOfWork.Save();
            _logger.LogInformation("Order {OrderId} shipped by user {UserId}.", OrderVM.OrderHeader.Id, GetCurrentUserId());
            TempData["Success"] = "Order Shipped Successfully.";
            return RedirectToAction("Details", "Order", new { orderId = OrderVM.OrderHeader.Id });
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        [ValidateAntiForgeryToken]
        public IActionResult CancelOrder()
        {
            var orderHeader = _unitOfWork.OrderHeader.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked: false);
            if (orderHeader == null)
            {
                return NotFound();
            }

            if (orderHeader.PaymentStatus == SD.PaymentStatusApproved)
            {
                if (UseLocalStripeFallback())
                {
                    _unitOfWork.OrderHeader.UpdateStatus(orderHeader.Id, SD.StatusCancelled, SD.StatusRefunded);
                }
                else
                {
                    if (!HasStripeApiKey())
                    {
                        _logger.LogWarning("Order refund blocked because Stripe is not configured. Order {OrderId}.", OrderVM.OrderHeader.Id);
                        TempData["error"] = "Refund processing is temporarily unavailable. Please review the payment configuration or contact support.";
                        return RedirectToAction("Details", "Order", new { orderId = OrderVM.OrderHeader.Id });
                    }

                    var options = new RefundCreateOptions
                    {
                        Reason = RefundReasons.RequestedByCustomer,
                        PaymentIntent = orderHeader.PaymentIntentId
                    };

                    var service = new RefundService();
                    Refund refund = service.Create(options);

                    _unitOfWork.OrderHeader.UpdateStatus(orderHeader.Id, SD.StatusCancelled, SD.StatusRefunded);
                }
            }
            else
            {
                _unitOfWork.OrderHeader.UpdateStatus(orderHeader.Id, SD.StatusCancelled, SD.StatusCancelled);
            }
            _unitOfWork.Save();

            _logger.LogInformation("Order {OrderId} cancelled by user {UserId}.", OrderVM.OrderHeader.Id, GetCurrentUserId());
            TempData["Success"] = "Order Cancelled Successfully.";
            return RedirectToAction("Details", "Order", new { orderId = OrderVM.OrderHeader.Id });
        }


        #region API CALLS
        [HttpGet]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult GetAll(string status)
        {
            IEnumerable<OrderHeader> orderHeaders;

            if (User.IsInRole(SD.Role_Admin) || User.IsInRole(SD.Role_Employee))
            {
                orderHeaders = _unitOfWork.OrderHeader.GetAll(includeProperties: "ApplicationUser");
            }
            else
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Challenge();
                }

                orderHeaders = _unitOfWork.OrderHeader.GetAll(u => u.ApplicationUserId == userId, includeProperties: "ApplicationUser");
            }

            switch (status)
            {
                case "pending":
                    orderHeaders = orderHeaders.Where(u =>
                        u.OrderStatus == SD.StatusPending ||
                        u.PaymentStatus == SD.PaymentStatusPending ||
                        u.PaymentStatus == SD.PaymentStatusDelayedPayment);
                    break;
                case "inprocess":
                    orderHeaders = orderHeaders.Where(u => u.OrderStatus == SD.StatusInProcess);
                    break;
                case "completed":
                case "shipped":
                    orderHeaders = orderHeaders.Where(u => u.OrderStatus == SD.StatusShipped);
                    break;
                case "cancelled":
                    orderHeaders = orderHeaders.Where(u => u.OrderStatus == SD.StatusCancelled || u.OrderStatus == SD.StatusRefunded);
                    break;
                case "approved":
                    orderHeaders = orderHeaders.Where(u => u.OrderStatus == SD.StatusApproved);
                    break;
                default:
                    break;
            }

            return Json(new { data = orderHeaders });
        }
        #endregion

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

        private bool CanAccessOrder(OrderHeader orderHeader)
        {
            if (User.IsInRole(SD.Role_Admin) || User.IsInRole(SD.Role_Employee))
            {
                return true;
            }

            var userId = GetCurrentUserId();
            return !string.IsNullOrWhiteSpace(userId) && orderHeader.ApplicationUserId == userId;
        }
    }
}
