namespace BulkyBook.Utility
{
    public enum EmailDeliveryStatus
    {
        Sent,
        NotConfigured,
        Failed
    }

    public sealed record EmailDeliveryResult(
        EmailDeliveryStatus Status,
        string Provider,
        string? DeliveryPath = null,
        string? ErrorMessage = null)
    {
        public bool Succeeded => Status == EmailDeliveryStatus.Sent;

        public static EmailDeliveryResult Sent(string provider, string? deliveryPath = null)
        {
            return new EmailDeliveryResult(EmailDeliveryStatus.Sent, provider, deliveryPath);
        }

        public static EmailDeliveryResult NotConfigured(string provider, string? errorMessage = null)
        {
            return new EmailDeliveryResult(EmailDeliveryStatus.NotConfigured, provider, ErrorMessage: errorMessage);
        }

        public static EmailDeliveryResult Failed(string provider, string errorMessage)
        {
            return new EmailDeliveryResult(EmailDeliveryStatus.Failed, provider, ErrorMessage: errorMessage);
        }
    }
}
