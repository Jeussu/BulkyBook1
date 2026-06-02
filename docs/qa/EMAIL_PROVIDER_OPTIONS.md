# Email Provider Options

This project supports `Email:Provider=LocalFile` for local-only development and `Email:Provider=Smtp` for real inbox delivery. Do not store real passwords, app passwords, API keys, or SMTP secrets in source control, docs, screenshots, or publish output.

## Current Implementation

| Provider | Config value | Sends to real inbox | Best use | Secret storage |
|---|---|---:|---|---|
| Local file outbox | `Email:Provider=LocalFile` | No | Local Development only | No secret required |
| SMTP | `Email:Provider=Smtp` | Yes | Gmail, Mailtrap SMTP, Brevo, SendGrid SMTP, Outlook SMTP, hosting SMTP | `dotnet user-secrets` locally; environment variables in Somee/staging/production |

## Gmail SMTP

Use Gmail only when the account is allowed to send through SMTP and you have an approved app password or organization-approved SMTP authentication method.

Required keys:

```text
Email:Provider = Smtp
Email:Smtp:Host = smtp.gmail.com
Email:Smtp:Port = 587
Email:Smtp:SecureSocketOptions = StartTls
Email:Smtp:UseStartTls = true
Email:Smtp:Username = <gmail-address>
Email:Smtp:Password = <gmail-app-password-or-approved-secret>
Email:Smtp:From = <gmail-address>
```

Google's Gmail Help documents `smtp.gmail.com`, STARTTLS port `587`, and authentication requirements: https://support.google.com/mail/answer/86377

## Mailtrap

Use Mailtrap Sandbox when you need safe test captures without sending to a real user inbox. Use Mailtrap Email Sending SMTP when you need real recipient delivery.

Recommended for QA:

- Sandbox: verify email content, links, subject, and delivery attempts without sending external messages.
- Email Sending SMTP: verify real inbox behavior after domain/sender setup.

## Brevo, SendGrid, Resend, Outlook, Hosting SMTP

The current code can use any provider that exposes SMTP. Configure the provider's SMTP host, port, security mode, username, password/token, and from address.

Common patterns:

- Port `587` with `Email:Smtp:SecureSocketOptions=StartTls`.
- Port `465` with `Email:Smtp:SecureSocketOptions=SslOnConnect`.
- Local SMTP tools with `Email:Smtp:SecureSocketOptions=None` and blank username/password.

## API-Based Providers

Some services prefer an HTTP API instead of SMTP. That is a valid future enhancement, but it should be implemented as a new provider such as `Email:Provider=ResendApi` or `SendGridApi`. This pass keeps the code targeted and schema-free by using SMTP only for real delivery.

## Decision Matrix

| Need | Recommended option |
|---|---|
| Local link testing without external mail | `LocalFile` |
| Real Gmail inbox test | Gmail SMTP |
| Safe QA capture without sending outside | Mailtrap Sandbox SMTP |
| Staging/production transactional email | Brevo, SendGrid, Mailtrap Email Sending, Outlook SMTP, or a verified hosting SMTP account |
| Higher reliability, tracking, templates, webhooks | Future API provider implementation |

## Troubleshooting

- If `.html` files are still created, runtime provider is still `LocalFile`; open `/Admin/Diagnostics/Email` in Development.
- If runtime provider is `Smtp` and no message arrives, check spam, sender verification, SMTP auth, STARTTLS/SSL mode, and provider rate limits.
- If the app says the account is unknown/already confirmed/unconfirmed, no email is intentionally sent for that branch.
