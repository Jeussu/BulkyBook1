// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;

namespace BulkyBookWeb.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResendEmailConfirmationModel : PageModel
    {
        private const string GenericSuccessMessage = "If the account exists and still needs confirmation, a confirmation message will be delivered by the configured email provider.";
        private const string LocalFileProviderName = "LocalFile";

        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailDeliveryService _emailDeliveryService;
        private readonly IWebHostEnvironment _environment;

        public ResendEmailConfirmationModel(
            UserManager<IdentityUser> userManager,
            IEmailDeliveryService emailDeliveryService,
            IWebHostEnvironment environment)
        {
            _userManager = userManager;
            _emailDeliveryService = emailDeliveryService;
            _environment = environment;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        public string StatusMessage { get; set; }

        public string DeliveryModeNotice =>
            IsDevelopmentLocalFileMode()
                ? "Development LocalFile email mode is active. Confirmation messages are saved as local .html files and are not sent to Gmail or any SMTP inbox."
                : string.Empty;

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            if (!_emailDeliveryService.IsConfigured)
            {
                StatusMessage = BuildStatusMessage(
                    EmailDeliveryResult.NotConfigured(_emailDeliveryService.ProviderName, "Email delivery is not configured."));
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null || await _userManager.IsEmailConfirmedAsync(user))
            {
                StatusMessage = GenericSuccessMessage;
                return Page();
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { userId = userId, code = code },
                protocol: Request.Scheme);
            var emailResult = await _emailDeliveryService.SendEmailWithResultAsync(
                Input.Email,
                "Confirm your email",
                $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

            StatusMessage = BuildStatusMessage(emailResult);
            return Page();
        }

        private string BuildStatusMessage(EmailDeliveryResult result)
        {
            if (result.Succeeded)
            {
                if (_environment.IsDevelopment() && !string.IsNullOrWhiteSpace(result.DeliveryPath))
                {
                    return $"Development LocalFile mode: no email was sent to Gmail or SMTP. Confirmation email was saved as a local .html file. Open this file in your browser: {result.DeliveryPath}";
                }

                return GenericSuccessMessage;
            }

            if (result.Status == EmailDeliveryStatus.NotConfigured)
            {
                return _environment.IsDevelopment()
                    ? "Email delivery is not configured. Set Email:Provider to LocalFile for development or Smtp for real delivery."
                    : "Email delivery is not configured. Please contact support.";
            }

            if (_environment.IsDevelopment() && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                return $"Email delivery failed. SMTP diagnostic: {result.ErrorMessage}. Check /Admin/Diagnostics/Email for sanitized runtime configuration.";
            }

            return "Email delivery failed. Please contact support or try again later.";
        }

        private bool IsDevelopmentLocalFileMode()
        {
            return _environment.IsDevelopment()
                && _emailDeliveryService.ProviderName.Equals(LocalFileProviderName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
