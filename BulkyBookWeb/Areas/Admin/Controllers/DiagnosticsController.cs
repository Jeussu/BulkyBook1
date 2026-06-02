using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BulkyBookWeb.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class DiagnosticsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly IEmailDeliveryService _emailDeliveryService;

        public DiagnosticsController(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            IEmailDeliveryService emailDeliveryService)
        {
            _configuration = configuration;
            _environment = environment;
            _emailDeliveryService = emailDeliveryService;
        }

        public IActionResult Email()
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            ViewData["EnvironmentName"] = _environment.EnvironmentName;
            ViewData["Provider"] = _emailDeliveryService.ProviderName;
            ViewData["IsConfigured"] = _emailDeliveryService.IsConfigured;
            ViewData["SmtpHost"] = _configuration["Email:Smtp:Host"] ?? string.Empty;
            ViewData["SmtpPort"] = _configuration["Email:Smtp:Port"] ?? string.Empty;
            ViewData["SecureSocketOptions"] = _configuration["Email:Smtp:SecureSocketOptions"] ?? string.Empty;
            ViewData["UseStartTls"] = _configuration["Email:Smtp:UseStartTls"] ?? string.Empty;
            ViewData["SmtpUsername"] = MaskEmail(_configuration["Email:Smtp:Username"]);
            ViewData["SmtpPasswordPresent"] = !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Password"]);
            ViewData["SmtpFrom"] = MaskEmail(_configuration["Email:Smtp:From"]);
            ViewData["LocalFileDirectory"] = _configuration["Email:LocalFile:Directory"] ?? string.Empty;

            return View();
        }

        private static string MaskEmail(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Not set";
            }

            var atIndex = value.IndexOf('@');
            if (atIndex <= 0)
            {
                return "Set";
            }

            var prefixLength = Math.Min(2, atIndex);
            return $"{value[..prefixLength]}***{value[atIndex..]}";
        }
    }
}
