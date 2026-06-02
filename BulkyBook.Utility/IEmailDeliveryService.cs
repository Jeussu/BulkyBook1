namespace BulkyBook.Utility
{
    public interface IEmailDeliveryService
    {
        bool IsConfigured { get; }
        string ProviderName { get; }
        Task<EmailDeliveryResult> SendEmailWithResultAsync(string email, string subject, string htmlMessage);
    }
}
