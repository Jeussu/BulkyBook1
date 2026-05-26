using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BulkyBook.Utility
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var host = _configuration["Email:Smtp:Host"];
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            var from = _configuration["Email:Smtp:From"];
            var port = _configuration.GetValue<int?>("Email:Smtp:Port") ?? 587;

            if (string.IsNullOrWhiteSpace(host) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(from))
            {
                _logger.LogWarning("SMTP settings are not configured. Skipping email to {Email}.", email);
                return;
            }

            var emailToSend = new MimeMessage();
            emailToSend.From.Add(MailboxAddress.Parse(from));
            emailToSend.To.Add(MailboxAddress.Parse(email));
            emailToSend.Subject = subject;
            emailToSend.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = htmlMessage };

            using (var emailClient = new SmtpClient())
            {
                await emailClient.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.StartTls);
                await emailClient.AuthenticateAsync(username, password);
                await emailClient.SendAsync(emailToSend);
                await emailClient.DisconnectAsync(true);
            }
        }
    }
}
