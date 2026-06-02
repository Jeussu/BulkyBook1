using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.Text;
using System.Text.Encodings.Web;

namespace BulkyBook.Utility
{
    public class EmailSender : IEmailSender, IEmailDeliveryService
    {
        private const string ProviderLocalFile = "LocalFile";
        private const string ProviderSmtp = "Smtp";
        private const string ProviderNotConfigured = "NotConfigured";

        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public bool IsConfigured
        {
            get
            {
                var provider = ResolveProvider();
                if (provider.Equals(ProviderLocalFile, StringComparison.OrdinalIgnoreCase))
                {
                    return _environment.IsDevelopment();
                }

                if (provider.Equals(ProviderSmtp, StringComparison.OrdinalIgnoreCase))
                {
                    return IsSmtpConfigured();
                }

                return false;
            }
        }

        public string ProviderName => ResolveProvider();

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            await SendEmailWithResultAsync(email, subject, htmlMessage);
        }

        public async Task<EmailDeliveryResult> SendEmailWithResultAsync(string email, string subject, string htmlMessage)
        {
            var provider = ResolveProvider();

            try
            {
                if (provider.Equals(ProviderLocalFile, StringComparison.OrdinalIgnoreCase))
                {
                    return await SendToLocalFileAsync(email, subject, htmlMessage);
                }

                if (provider.Equals(ProviderSmtp, StringComparison.OrdinalIgnoreCase))
                {
                    return await SendBySmtpAsync(email, subject, htmlMessage);
                }

                _logger.LogWarning("Email delivery is not configured. Skipping email to {Email}.", email);
                return EmailDeliveryResult.NotConfigured(ProviderNotConfigured, "Email delivery is not configured.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email delivery failed for {Email}.", email);
                return EmailDeliveryResult.Failed(provider, BuildSafeErrorMessage(ex));
            }
        }

        private async Task<EmailDeliveryResult> SendToLocalFileAsync(string email, string subject, string htmlMessage)
        {
            if (!_environment.IsDevelopment())
            {
                _logger.LogWarning("Local file email provider is only allowed in Development. Skipping email to {Email}.", email);
                return EmailDeliveryResult.NotConfigured(ProviderLocalFile, "Local file email provider is only allowed in Development.");
            }

            var outboxDirectory = GetLocalOutboxDirectory();
            Directory.CreateDirectory(outboxDirectory);

            var fileName = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}_{BuildSafeEmailToken(email)}_{Guid.NewGuid():N}.html";
            var deliveryPath = Path.Combine(outboxDirectory, fileName);
            var html = BuildLocalEmailHtml(email, subject, htmlMessage);

            await File.WriteAllTextAsync(deliveryPath, html, Encoding.UTF8);

            _logger.LogInformation("Email written to local outbox file {Path}.", deliveryPath);
            return EmailDeliveryResult.Sent(ProviderLocalFile, deliveryPath);
        }

        private async Task<EmailDeliveryResult> SendBySmtpAsync(string email, string subject, string htmlMessage)
        {
            var host = _configuration["Email:Smtp:Host"];
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            var from = _configuration["Email:Smtp:From"];
            var port = _configuration.GetValue<int?>("Email:Smtp:Port") ?? 587;

            if (!IsSmtpConfigured())
            {
                _logger.LogWarning("SMTP settings are not configured. Skipping email to {Email}.", email);
                return EmailDeliveryResult.NotConfigured(ProviderSmtp, "SMTP settings are not configured.");
            }

            var emailToSend = new MimeMessage();
            emailToSend.From.Add(MailboxAddress.Parse(from!));
            emailToSend.To.Add(MailboxAddress.Parse(email));
            emailToSend.Subject = subject;
            emailToSend.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = htmlMessage };

            using (var emailClient = new SmtpClient())
            {
                var secureSocketOptions = ResolveSecureSocketOptions(port);
                await emailClient.ConnectAsync(host!, port, secureSocketOptions);
                if (!string.IsNullOrWhiteSpace(username))
                {
                    await emailClient.AuthenticateAsync(username, password!);
                }

                await emailClient.SendAsync(emailToSend);
                await emailClient.DisconnectAsync(true);
            }

            _logger.LogInformation("Email sent to {Email} using SMTP.", email);
            return EmailDeliveryResult.Sent(ProviderSmtp);
        }

        private SecureSocketOptions ResolveSecureSocketOptions(int port)
        {
            var configuredValue = _configuration["Email:Smtp:SecureSocketOptions"];
            if (!string.IsNullOrWhiteSpace(configuredValue))
            {
                if (Enum.TryParse<SecureSocketOptions>(configuredValue, ignoreCase: true, out var configuredOption))
                {
                    return configuredOption;
                }

                _logger.LogWarning(
                    "Invalid Email:Smtp:SecureSocketOptions value '{SecureSocketOptions}'. Falling back to Email:Smtp:UseStartTls.",
                    configuredValue);
            }

            var useStartTls = _configuration.GetValue<bool?>("Email:Smtp:UseStartTls");
            if (useStartTls.HasValue)
            {
                return useStartTls.Value ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            }

            return port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        }

        private string BuildSafeErrorMessage(Exception exception)
        {
            var message = $"{exception.GetType().Name}: {exception.Message}";
            var valuesToRedact = new[]
            {
                _configuration["Email:Smtp:Username"],
                _configuration["Email:Smtp:Password"]
            };

            foreach (var value in valuesToRedact)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    message = message.Replace(value, "<redacted>", StringComparison.OrdinalIgnoreCase);
                }
            }

            message = message.ReplaceLineEndings(" ");
            return message.Length <= 500 ? message : message[..500];
        }

        private string ResolveProvider()
        {
            var provider = _configuration["Email:Provider"];
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return provider.Trim();
            }

            return IsSmtpConfigured() ? ProviderSmtp : ProviderNotConfigured;
        }

        private bool IsSmtpConfigured()
        {
            var host = _configuration["Email:Smtp:Host"];
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            var from = _configuration["Email:Smtp:From"];

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            {
                return false;
            }

            var hasUsername = !string.IsNullOrWhiteSpace(username);
            var hasPassword = !string.IsNullOrWhiteSpace(password);
            return hasUsername == hasPassword;
        }

        private string GetLocalOutboxDirectory()
        {
            var configuredDirectory = _configuration["Email:LocalFile:Directory"];
            var defaultDirectory = Path.Combine(_environment.ContentRootPath, "App_Data", "dev-mails");
            if (string.IsNullOrWhiteSpace(configuredDirectory))
            {
                return defaultDirectory;
            }

            var directory = configuredDirectory.Trim();
            var fullDirectory = Path.IsPathRooted(directory)
                ? Path.GetFullPath(directory)
                : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, directory));

            var contentRoot = Path.GetFullPath(_environment.ContentRootPath);
            if (!fullDirectory.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Configured local email outbox directory is outside ContentRootPath. Using default directory.");
                return defaultDirectory;
            }

            return fullDirectory;
        }

        private static string BuildSafeEmailToken(string email)
        {
            var builder = new StringBuilder();
            foreach (var character in email.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
                else if (character == '@')
                {
                    builder.Append("_at_");
                }
                else if (character is '.' or '-' or '_')
                {
                    builder.Append(character);
                }
            }

            if (builder.Length == 0)
            {
                return "email";
            }

            return builder.Length <= 60 ? builder.ToString() : builder.ToString()[..60];
        }

        private static string BuildLocalEmailHtml(string email, string subject, string htmlMessage)
        {
            var encoder = HtmlEncoder.Default;
            return $"""
                <!doctype html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <title>{encoder.Encode(subject)}</title>
                </head>
                <body>
                    <h1>Local development email</h1>
                    <dl>
                        <dt>To</dt>
                        <dd>{encoder.Encode(email)}</dd>
                        <dt>Subject</dt>
                        <dd>{encoder.Encode(subject)}</dd>
                        <dt>Created UTC</dt>
                        <dd>{DateTime.UtcNow:O}</dd>
                    </dl>
                    <hr>
                    {htmlMessage}
                </body>
                </html>
                """;
        }
    }
}
