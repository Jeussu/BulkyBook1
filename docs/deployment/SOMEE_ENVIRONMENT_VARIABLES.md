# Somee Environment Variables

Use host-level configuration or `web.config` environment variables. Do not put real secrets in source control, docs, screenshots, or publish output.

## Required

| Name | Example / placeholder | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Required for production behavior. |
| `ConnectionStrings__DefaultConnection` | `<somee-ms-sql-connection-string>` | Required before first launch against Somee DB. |
| `Database__AutoMigrate` | `false` | Keep disabled; apply reviewed SQL manually. |
| `SeedData__EnableDemoData` | `false` | Never seed local demo catalog/users in staging/production. |
| `Application__BaseUrl` | `https://<your-somee-domain>` | Required for checkout redirect URLs. |
| `Stripe__EnableLocalCheckoutFallback` | `false` | Keep local fallback disabled outside local development. |

## Optional Integrations

| Name | Example / placeholder | Notes |
|---|---|---|
| `Stripe__PublishableKey` | `<stripe-test-publishable-key-if-used>` | Optional staging/test checkout only. |
| `Stripe__SecretKey` | `<stripe-test-secret-key-if-used>` | Optional; keep secret outside source control. |
| `Stripe__WebhookSecret` | `<stripe-webhook-secret-if-webhook-is-added>` | Future use; current production webhook is not complete. |
| `Authentication__Facebook__AppId` | `<facebook-app-id-if-used>` | Optional; Facebook provider is disabled if blank. |
| `Authentication__Facebook__AppSecret` | `<facebook-app-secret-if-used>` | Optional; keep secret outside source control. |
| `Email__Smtp__Host` | `<smtp-host-if-used>` | Optional; email sender is no-op if SMTP is incomplete. |
| `Email__Smtp__Port` | `587` | Optional. |
| `Email__Smtp__Username` | `<smtp-username-if-used>` | Optional. |
| `Email__Smtp__Password` | `<smtp-password-if-used>` | Optional; keep secret outside source control. |
| `Email__Smtp__From` | `<sender-email-if-used>` | Optional but needed for real outbound mail. |

## Pre-Launch Check

- Confirm `ConnectionStrings__DefaultConnection` points to the intended Somee SQL database.
- Confirm `Database__AutoMigrate=false`.
- Confirm `SeedData__EnableDemoData=false`.
- Confirm no Stripe/Facebook/SMTP secret values are stored in committed files.
- Confirm `Application__BaseUrl` uses the final HTTPS Somee domain.
