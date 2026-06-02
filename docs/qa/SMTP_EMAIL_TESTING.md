# SMTP Email Testing

Use this guide when Forgot Password and Resend Email Confirmation must deliver to a real inbox such as Gmail, Outlook, Mailtrap, SendGrid, or another SMTP provider.

## Rules

- Do not put real SMTP passwords or Gmail app passwords in `appsettings.json`, docs, screenshots, or source code.
- For local development, store SMTP values with `dotnet user-secrets`.
- For Somee, staging, or production, store SMTP values as host environment variables.
- `Email:Provider=LocalFile` never sends to Gmail or any inbox. It only writes local `.html` files.
- Real inbox delivery requires `Email:Provider=Smtp`.

## Local Gmail SMTP Setup

Run these commands from the repository root and replace placeholders with your real values. Do not paste the real values into commits, screenshots, or bug reports.

```powershell
dotnet user-secrets set "Email:Provider" "Smtp" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Port" "587" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:SecureSocketOptions" "StartTls" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:UseStartTls" "true" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Username" "<gmail-address>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Password" "<gmail-app-password-or-approved-secret>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:From" "<gmail-address>" --project BulkyBookWeb
```

Google documents Gmail SMTP as `smtp.gmail.com`, STARTTLS port `587`, and authenticated delivery in Gmail Help: https://support.google.com/mail/answer/86377

Restart the app after setting secrets so configuration is reloaded.

## Runtime Configuration Check

In local Development, sign in as an Admin and open:

```text
/Admin/Diagnostics/Email
```

Expected for real inbox testing:

- `Environment` is `Development`.
- `Provider` is `Smtp`.
- `IsConfigured` is `True`.
- `SMTP password present` is `True` when the provider requires authentication.
- The username and from address are masked.
- No password, connection string, Stripe key, or SMTP secret is displayed.

If this page still shows `Provider=LocalFile`, the app is not using the SMTP settings you just configured. Restart the app and confirm that the secrets were set for the `BulkyBookWeb` project. If the page shows `Provider=Smtp` but delivery fails, use the sanitized SMTP error shown by Forgot Password or Resend Email Confirmation and verify the provider account settings.

## Other SMTP Providers

For STARTTLS providers:

```powershell
dotnet user-secrets set "Email:Provider" "Smtp" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Host" "<smtp-host>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Port" "587" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:SecureSocketOptions" "StartTls" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Username" "<smtp-username>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Password" "<smtp-secret>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:From" "<from-address>" --project BulkyBookWeb
```

For SSL-on-connect providers on port `465`:

```powershell
dotnet user-secrets set "Email:Smtp:Port" "465" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:SecureSocketOptions" "SslOnConnect" --project BulkyBookWeb
```

For unauthenticated local SMTP tools such as Papercut or MailHog:

```powershell
dotnet user-secrets set "Email:Provider" "Smtp" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Host" "localhost" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Port" "<local-smtp-port>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:SecureSocketOptions" "None" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Username" "" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Password" "" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:From" "no-reply@localhost" --project BulkyBookWeb
```

## Verify Resend Email Confirmation

Preconditions:

- App is running after SMTP secrets are set.
- Target account exists.
- `AspNetUsers.EmailConfirmed = 0` for the target account.

Steps:

1. Open `/Identity/Account/ResendEmailConfirmation`.
2. Confirm the LocalFile notice is not shown.
3. Enter the target email address and submit.
4. Check the recipient inbox and spam folder.
5. Click the confirmation link from the real email.
6. Verify `AspNetUsers.EmailConfirmed = 1`.

SQL verification:

```sql
DECLARE @Email nvarchar(256) = N'<target-email>';

SELECT Id, Email, EmailConfirmed
FROM AspNetUsers
WHERE NormalizedEmail = UPPER(@Email);
```

Expected result:

- A real SMTP email arrives in the target inbox.
- No new LocalFile `.html` file is required for this flow.
- Confirmation link updates `EmailConfirmed` to `1`.

Note: Resend Email Confirmation intentionally does not send a message for an unknown account or an account that is already confirmed. The UI stays generic to avoid account enumeration.

## Verify Forgot Password

Preconditions:

- App is running after SMTP secrets are set.
- Target account exists.
- `AspNetUsers.EmailConfirmed = 1` for the target account.

Steps:

1. Capture current `PasswordHash` and `SecurityStamp`.
2. Open `/Identity/Account/ForgotPassword`.
3. Confirm the LocalFile notice is not shown.
4. Enter the target email address and submit.
5. Check the recipient inbox and spam folder.
6. Click the reset link from the real email.
7. Complete the reset with a temporary QA password.
8. Log in with the new password.
9. Verify `PasswordHash` and `SecurityStamp` changed.

SQL verification:

```sql
DECLARE @Email nvarchar(256) = N'<target-email>';

SELECT Id, Email, EmailConfirmed, PasswordHash, SecurityStamp
FROM AspNetUsers
WHERE NormalizedEmail = UPPER(@Email);
```

Expected result:

- A real SMTP email arrives in the target inbox.
- Reset token works once.
- User can log in with the new password.
- `PasswordHash` and `SecurityStamp` change after reset.

Note: Forgot Password intentionally does not send a message for an unknown account or an unconfirmed account. The UI stays generic to avoid account enumeration.

## Switch Back To LocalFile

Use this when you want local `.html` files again instead of real SMTP delivery.

```powershell
dotnet user-secrets set "Email:Provider" "LocalFile" --project BulkyBookWeb
dotnet user-secrets set "Email:LocalFile:Directory" "App_Data/dev-mails" --project BulkyBookWeb
```

Restart the app after switching provider.
