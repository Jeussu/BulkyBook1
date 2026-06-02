# Somee Pre-Deploy Checklist

Do not commit secrets, create migrations, run `dotnet ef database update`, reset DB, or reseed DB during this checklist.

## Local Build

```powershell
git branch --show-current
git status --short
dotnet build BulkyBook.sln --configuration Release --nologo
git diff --check
```

Expected:

- Build has `0 Error(s)`.
- `git diff --check` reports no whitespace errors.
- Any warnings are reviewed and logged in the bug register.

## Environment Variables

Required Somee values:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ConnectionStrings__DefaultConnection=<somee-ms-sql-connection-string>`
- `Database__AutoMigrate=false`
- `SeedData__EnableDemoData=false`
- `Application__BaseUrl=https://<your-somee-domain>`
- `Stripe__EnableLocalCheckoutFallback=false`
- `Email__Provider=Smtp`
- `Email__Smtp__Host=<smtp-host>`
- `Email__Smtp__Port=587`
- `Email__Smtp__SecureSocketOptions=StartTls`
- `Email__Smtp__UseStartTls=true`
- `Email__Smtp__Username=<smtp-username-if-required>`
- `Email__Smtp__Password=<smtp-password-if-required>`
- `Email__Smtp__From=<sender-email>`

Optional values:

- `Stripe__PublishableKey`
- `Stripe__SecretKey`
- `Stripe__WebhookSecret` only if a webhook implementation is added later
- `Authentication__Facebook__AppId`
- `Authentication__Facebook__AppSecret`

## Publish Output Checks

- [ ] Publish with `Release`.
- [ ] Publish output does not include `appsettings.Development.json`.
- [ ] Publish output does not include `App_Data/dev-mails/*.html`.
- [ ] Publish output does not include real SMTP, Stripe, Facebook, DB, or user password secrets.
- [ ] `web.config` environment variables, if used, contain placeholders in docs and real values only in hosting configuration.

## Database Checks

- [ ] Somee DB exists and is reachable from hosting.
- [ ] DB schema is already compatible with current migrations.
- [ ] No pending schema change is introduced by this pass.
- [ ] Production `SeedAdmin` values are blank unless intentionally creating the first admin.
- [ ] Demo seed is disabled.

## Deployment Blockers

Block deployment if any item is true:

- Any open P0 bug in `docs/qa/BULKYBOOK_PRE_DEPLOY_BUG_REGISTER.md`.
- SMTP provider is not configured for real Identity email.
- Stripe is required but keys/config are missing.
- App cannot connect to intended Somee DB.
- Publish output contains a real secret.
- Any user flow requires DB reset/reseed to work.
