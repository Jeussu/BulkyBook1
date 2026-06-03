# Somee Staging Deployment Runbook

Use this runbook to deploy the BulkyBook full ASP.NET Core MVC/Razor UI app to a Somee staging/demo site. Do not commit secrets, do not run `dotnet ef database update`, do not reset/reseed the database, and do not store real passwords or keys in source files.

## Local Artifacts

Current staging artifacts prepared by this pass:

- DB review script: `migrate-somee.sql`
- Publish package folder: `publish-somee-fallback-fix`

If you create a new package later, use a fresh folder and update the evidence notes. Do not upload source folders or local test evidence to the web root.

## Required Somee Environment Variables

Configure these in Somee host environment settings or protected hosting configuration. Use real values only in Somee, never in committed files or screenshots.

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<somee-ms-sql-connection-string>
Database__AutoMigrate=false
SeedData__EnableDemoData=false
Application__BaseUrl=https://<your-somee-domain>
Stripe__EnableLocalCheckoutFallback=true
Email__Provider=Smtp
Email__Smtp__Host=smtp.gmail.com
Email__Smtp__Port=587
Email__Smtp__SecureSocketOptions=StartTls
Email__Smtp__UseStartTls=true
Email__Smtp__Username=<smtp-username>
Email__Smtp__Password=<smtp-password-or-app-password>
Email__Smtp__From=<sender-email>
```

Optional integration variables:

```text
Stripe__PublishableKey=<stripe-test-publishable-key>
Stripe__SecretKey=<stripe-test-secret-key>
Authentication__Facebook__AppId=<facebook-app-id>
Authentication__Facebook__AppSecret=<facebook-app-secret>
```

## Configuration Rules

- Do not use `Email__Provider=LocalFile` on Somee. `LocalFile` only writes local `.html` files and never sends to Gmail or any real inbox.
- `Stripe__EnableLocalCheckoutFallback=true` is allowed only for this personal Somee demo/staging site when real Stripe keys are not configured. It creates a no-payment demo order and must not be treated as production payment confirmation.
- For real commerce or production payment testing, set `Stripe__EnableLocalCheckoutFallback=false` and configure valid Stripe test/live keys instead.
- Do not enable `SeedData__EnableDemoData` in staging/production.
- Do not enable `Database__AutoMigrate` in staging/production.
- `Application__BaseUrl` must match the final HTTPS Somee domain.
- If Stripe keys are configured and valid, the app uses normal Stripe checkout. If Stripe is missing/invalid and `Stripe__EnableLocalCheckoutFallback=true`, the app uses the explicit demo/staging fallback.
- If Stripe is enabled, configure Stripe success/cancel URLs and dashboard settings for the Somee HTTPS domain.
- If Facebook login is enabled, configure Facebook redirect/callback URLs for the Somee HTTPS domain.
- Secrets must live in Somee host environment variables or protected hosting config, not in `appsettings.json`, docs, screenshots, or source code.

## Database Preparation

1. Create the Somee MS SQL database.
2. Get the Somee SQL connection string.
3. Review `migrate-somee.sql` locally before applying it.
4. Apply `migrate-somee.sql` manually to the Somee DB using Somee SQL tools or SQL Server Management Studio.
5. Confirm the DB has the expected tables, including `AspNetUsers`, `Products`, `ShoppingCarts`, `OrderHeaders`, and `__EFMigrationsHistory`.

Do not run `dotnet ef database update` for Somee in this pass.

## Upload Steps

1. Create the Somee website.
2. Create the Somee MS SQL database.
3. Configure the required Somee environment variables.
4. Apply `migrate-somee.sql` manually after review.
5. Upload the contents of `publish-somee-fallback-fix` to the Somee web root via FTP or File Manager.
6. Restart the Somee app/site.
7. Open `https://<your-somee-domain>`.
8. Run `docs/deployment/SOMEE_STAGING_SMOKE_TEST_EXECUTION.md`.

## What To Upload

Upload the contents of the publish folder, not the repository root:

- `BulkyBookWeb.dll`
- `BulkyBookWeb.runtimeconfig.json`
- `BulkyBookWeb.deps.json`
- `web.config`
- `appsettings.json`
- `appsettings.Production.json`
- `wwwroot/`
- All required dependency `.dll` files from the publish folder

## What Not To Upload

- `.git/`
- Source folders such as `BulkyBookWeb/`, `BulkyBook.DataAccess/`, `BulkyBook.Models/`, `BulkyBook.Utility/`
- `appsettings.Development.json`
- `BulkyBookWeb/App_Data/dev-mails/`
- Any local `.html` email outbox files
- `migrate-somee.sql` into the web root
- `docs/`, `tools/`, screenshots, local test evidence, or user-secrets files
- Any file containing real SMTP, Gmail, Stripe, Facebook, DB, or user password values

## appsettings.json Handling

The publish output includes `appsettings.json` and `appsettings.Production.json` with blank/placeholder production values. Upload them as part of the app package, but do not edit real secrets into these files. Somee environment variables should override connection string, SMTP, Stripe, Facebook, and domain settings.

## Local Dev Mails

`App_Data/dev-mails` is local-only. It should not be present in the publish output and should never be uploaded to Somee. Real staging email requires `Email__Provider=Smtp`.

## Evidence Rules

Capture evidence without secrets:

- Use screenshots that mask SMTP password, DB connection string, Stripe key, Facebook secret, and Identity token URLs.
- For email tests, capture inbox arrival and subject, not the full confirmation/reset URL.
- For DB checks, capture table/row status and IDs only; do not capture credentials.
- For Somee settings, capture key names and whether values are present, not the values themselves.

## Blocking Conditions

Block upload or stop testing if any condition is found:

- Publish output contains a real secret.
- `appsettings.Development.json` is in publish output.
- `App_Data/dev-mails` or local outbox `.html` files are in publish output.
- `web.config` is missing.
- `wwwroot` static assets are missing.
- Somee DB connection string is missing.
- SMTP env vars are missing when Identity email is required.
- Stripe is required but test keys/domain URLs are not configured and `Stripe__EnableLocalCheckoutFallback` is not intentionally enabled for demo/staging.
