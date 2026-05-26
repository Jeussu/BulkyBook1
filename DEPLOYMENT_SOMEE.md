# BulkyBook Somee Deployment Preparation

This guide prepares a manual deployment to Somee Free ASP.NET Hosting using ASP.NET Core, IIS, and MS SQL Express. Do not commit real secrets or generated publish output.

## Prerequisites

- .NET SDK 8 or newer on the local machine.
- EF Core CLI available through `dotnet ef`.
- Somee account, ASP.NET website, and MS SQL database.
- Future Somee HTTPS domain, for example `https://your-site.somee.com`.
- Stripe test keys only if testing checkout.
- Facebook app only if testing OAuth login.

## Required Somee / Production Configuration

Set these values through Somee configuration, `web.config` environment variables, or another host-supported secret mechanism:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<somee-ms-sql-connection-string>
Database__AutoMigrate=false
Application__BaseUrl=https://<your-somee-domain>
SeedData__EnableDemoData=false
Stripe__EnableLocalCheckoutFallback=false
Stripe__PublishableKey=<stripe-test-publishable-key-if-used>
Stripe__SecretKey=<stripe-test-secret-key-if-used>
Stripe__WebhookSecret=<stripe-webhook-secret-if-webhook-is-added>
Authentication__Facebook__AppId=<facebook-app-id-if-used>
Authentication__Facebook__AppSecret=<facebook-app-secret-if-used>
Email__Smtp__Host=<smtp-host-if-used>
Email__Smtp__Port=587
Email__Smtp__Username=<smtp-username-if-used>
Email__Smtp__Password=<smtp-password-if-used>
Email__Smtp__From=<sender-email-if-used>
```

Leave Stripe, Facebook, and SMTP values empty if those integrations are not being tested. Missing Facebook config does not register the provider. Missing SMTP config makes email sending a safe no-op.

## Local Pre-Publish Checks

```powershell
dotnet restore BulkyBook.sln
dotnet build BulkyBook.sln
dotnet ef migrations list --project BulkyBook.DataAccess --startup-project BulkyBookWeb --context ApplicationDbContext
dotnet publish BulkyBookWeb -c Release -o ./publish-somee
```

Do not commit `publish-somee` or any publish output.

## Database Deployment

Production auto-migration is disabled by default. Generate an idempotent migration script locally:

```powershell
dotnet ef migrations script --project BulkyBook.DataAccess --startup-project BulkyBookWeb --context ApplicationDbContext --idempotent -o migrate-somee.sql
```

Review the script, then apply it to the Somee MS SQL database using Somee SQL tools or SSMS. Do not commit `migrate-somee.sql` unless intentionally requested.

Somee free databases have size/resource limits. Keep demo data small and disable `SeedData__EnableDemoData` in Production.

## IIS / Publish Output

`dotnet publish` generates output suitable for IIS / ASP.NET Core Module hosting, including `web.config`. Upload the publish output contents to the Somee web root. The app should not bind to local ports in IIS; IIS/ANCM owns the binding.

## Stripe Notes

For Somee staging, use Stripe test keys only. `Application__BaseUrl` must match the public HTTPS Somee domain so success and cancel URLs point back to the deployed site.

Current limitation: the project does not have a production Stripe webhook endpoint. Real payment production should wait until webhook signature validation and idempotent payment updates are implemented.

## Facebook OAuth Notes

Expected callback URL:

```text
https://<your-somee-domain>/signin-facebook
```

Add that URI in the Facebook Developer Console. Use HTTPS, configure test users if the Facebook app is not live, and keep `Authentication__Facebook__AppSecret` outside source control.

## File Upload Notes

Product uploads are stored under `wwwroot/images/products` on the hosting filesystem. This is acceptable for a small Somee staging/portfolio demo, but free hosting storage is limited. Move uploads to durable object storage before production use.

Static SVG demo covers under `wwwroot/images/products/book-covers` should be included in publish output.

## Not Production Ready Yet

- Stripe webhook and payment idempotency.
- Real payment/refund hardening.
- Decimal money migration.
- Persistent external file storage.
- Automated test coverage.
- Full production monitoring/backups.
