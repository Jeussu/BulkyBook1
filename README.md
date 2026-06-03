# BulkyBook - ASP.NET Core MVC Bookstore

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![ASP.NET Core MVC](https://img.shields.io/badge/ASP.NET%20Core-MVC-512BD4)
![EF Core](https://img.shields.io/badge/EF%20Core-8.x-6DB33F)
![SQL Server](https://img.shields.io/badge/SQL%20Server-EF%20Core%20Provider-CC2927?logo=microsoftsqlserver&logoColor=white)

BulkyBook is a technical bookstore / e-commerce demo app built with ASP.NET Core MVC, Razor Views, Razor Pages Identity, Entity Framework Core, SQL Server, and Stripe Checkout. It is a full UI web application, not an API-only project.

The project started as an older BulkyBook-style learning app and has been modernized into a portfolio/demo/staging-ready web app with role-based storefront, cart, checkout, order management, real SMTP email support, and Somee staging deployment documentation.

## Project Status

| Area | Status | Notes |
| --- | --- | --- |
| Local demo | Ready | Runs locally on .NET 8 with SQL Server LocalDB / SQL Server and demo seed data. |
| Portfolio review | Ready | Suitable for code review, screenshots, and UI walkthroughs. |
| Somee staging/demo | Deployed / staging demo | Free-hosting staging site has been uploaded and can load the UI after correcting the web root upload folder. |
| Production | Not fully production-ready | Requires full deployed-domain smoke evidence and production hardening before real users/payments. |
| Stripe payments | Demo/staging only | Stripe Checkout integration exists, but webhook/idempotency hardening is still a known production gap. |
| Email | SMTP Gmail tested locally | Forgot Password and Resend Email Confirmation were verified with Gmail SMTP locally. Somee requires configured SMTP host environment / `web.config` values. |
| Facebook login | Implemented, requires configuration | Requires production `AppId`, `AppSecret`, and a valid HTTPS redirect URI. |
| Database | EF schema prepared | Somee DB can be initialized with reviewed migration SQL plus optional demo seed SQL. |

## Staging Demo

Somee staging/demo domain:

```text
http://vinh-bulkybook.somee.com
```

This is a free-hosting demo/staging environment, not guaranteed production-grade hosting. Somee free hosting may sleep, respond slowly, or require periodic visits to wake the app. Do not rely on it for 24/7 production availability.

Deployment notes from the current staging work:

- Somee website created.
- Somee MS SQL database created: `bulkybookdb`.
- EF schema script applied manually.
- Demo seed data imported.
- App files uploaded to Somee.
- UI can load after uploading the publish contents to the correct web root.
- If the deployed catalog shows 0 products, verify that the seed SQL was applied and that the app points to the intended Somee DB.

## Screenshots / UI Overview

Screenshots are not committed yet.

TODO screenshot set:

- Catalog / home page.
- Product detail page.
- Cart.
- Checkout summary.
- Admin product management.
- Order management.

Do not add screenshots with private local paths, secrets, connection strings, token URLs, or passwords.

## Features

### Public Storefront

- Browse the book catalog.
- Search by title, author, or ISBN.
- Filter by category, cover type, and price range.
- Sort and paginate catalog results.
- View product detail pages with image, description, stock state, pricing, and related books.
- Out-of-stock products are handled in the storefront and checkout flow.

### Customer / Individual User

- Register and log in with ASP.NET Core Identity.
- Confirm email.
- Request Forgot Password / Reset Password email.
- Add products to cart.
- Update or remove cart quantities.
- Checkout through the individual payment flow.
- View `My Orders`.
- Customer order isolation: customers should only see their own orders.

### Company User

- Company-linked account support.
- Company checkout flow.
- Delayed-payment order state.
- `PaymentDueDate` handling for delayed-payment orders.

### Admin / Employee

- Product CRUD.
- Category CRUD.
- Cover type CRUD.
- Company CRUD.
- Order management.
- Role-based navigation and authorization.
- Employees can work with order management without full catalog administration access.

### Email

- `LocalFile` provider for local development evidence.
- `Smtp` provider for real inbox delivery.
- Gmail SMTP with app password tested locally.
- Forgot Password and Resend Email Confirmation are real-email capable when `Email:Provider=Smtp` and SMTP settings are configured.

### Deployment

- Somee / IIS deployment package support.
- MS SQL schema script support.
- Environment-variable / `web.config` based configuration.
- Deployment smoke test docs.

## Tech Stack

- .NET 8
- ASP.NET Core MVC
- Razor Views
- Razor Pages for ASP.NET Core Identity UI
- Entity Framework Core 8
- SQL Server / SQL Server LocalDB / Somee MS SQL
- ASP.NET Core Identity with roles
- Bootstrap / Bootswatch
- jQuery
- DataTables
- Toastr
- SweetAlert2
- TinyMCE for product descriptions
- Stripe.net SDK
- Facebook OAuth provider
- MailKit / MimeKit for SMTP email

## Architecture / Solution Structure

```text
BulkyBook1/
|-- BulkyBook.sln
|-- BulkyBookWeb/              MVC UI, Razor Pages Identity, controllers, views, static assets
|-- BulkyBook.Models/          Entities and view models
|-- BulkyBook.DataAccess/      EF Core DbContext, repositories, migrations, seeders
|-- BulkyBook.Utility/         Constants, email delivery, Stripe/payment helpers
|-- docs/                      QA and deployment runbooks
|-- tools/                     QA/deployment helper scripts
|-- DEPLOYMENT_SOMEE.md        Earlier Somee deployment preparation notes
`-- README.md
```

| Project / folder | Purpose |
| --- | --- |
| `BulkyBookWeb` | ASP.NET Core MVC UI, Razor Views, Razor Pages Identity, controllers, `Program.cs`, static assets under `wwwroot`. |
| `BulkyBook.Models` | Domain entities and view models. |
| `BulkyBook.DataAccess` | EF Core `ApplicationDbContext`, repositories, migrations, database initializer, demo data seeder. |
| `BulkyBook.Utility` | Shared constants, email delivery abstractions, Stripe settings, utility helpers. |
| `docs` | QA packages, SMTP testing docs, Somee deployment runbooks, smoke tests, signoff checklists. |
| `tools` | Local QA, deployment readiness, and migration script helper scripts. |

## Local Setup

### Prerequisites

- .NET SDK 8.x.
- SQL Server LocalDB or SQL Server.
- EF Core CLI tools.

Install or update EF tools if needed:

```powershell
dotnet tool install --global dotnet-ef
dotnet tool update --global dotnet-ef
```

### Restore and Build

```powershell
dotnet restore
dotnet build BulkyBook.sln --configuration Release --nologo
```

### Configure Local Database

Use `BulkyBookWeb/appsettings.Development.json` for local-only development values, or configure a local connection string with user-secrets / environment variables.

Run local migrations only when you intentionally want to update the local development database:

```powershell
dotnet ef database update --project BulkyBook.DataAccess --startup-project BulkyBookWeb
```

### Run the App

```powershell
dotnet run --project BulkyBookWeb
```

The default local URL depends on `BulkyBookWeb/Properties/launchSettings.json` or the Visual Studio launch profile. Common local URLs are:

```text
https://localhost:7206
http://localhost:5206
```

## Email Configuration

`LocalFile` is useful for local evidence, but it does not send to Gmail or any inbox. It writes `.html` files under:

```text
BulkyBookWeb/App_Data/dev-mails
```

Real inbox delivery requires:

```text
Email:Provider=Smtp
```

Example local Gmail SMTP setup using `dotnet user-secrets` with placeholders only:

```powershell
dotnet user-secrets set "Email:Provider" "Smtp" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Port" "587" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:SecureSocketOptions" "StartTls" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:UseStartTls" "true" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Username" "<gmail-address>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Password" "<gmail-app-password>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:From" "<gmail-address>" --project BulkyBookWeb
```

Never commit real SMTP passwords, Gmail app passwords, Stripe keys, Facebook secrets, database passwords, or private tokens.

More details:

- [SMTP Email Testing](docs/qa/SMTP_EMAIL_TESTING.md)
- [Email Provider Options](docs/qa/EMAIL_PROVIDER_OPTIONS.md)

## Database and Demo Data

The demo seed includes:

- Categories.
- Cover types.
- Companies.
- 20 technical book products.
- Demo carts and orders for local testing.

If the deployed catalog shows 0 books, verify:

- The app connection string points to the intended Somee DB.
- `Products` has rows.
- `Categories` and `CoverTypes` exist.
- Product images exist under `wwwroot/images/products/book-covers-modern/`.

Safe verification SQL:

```sql
SELECT COUNT(*) AS ProductCount FROM Products;
SELECT COUNT(*) AS CategoryCount FROM Categories;
SELECT COUNT(*) AS CoverTypeCount FROM CoverTypes;
SELECT COUNT(*) AS CompanyCount FROM Companies;
```

For staging/demo database preparation, use reviewed SQL scripts only. Do not run automatic production database updates unless a deployment plan explicitly calls for it.

## Somee Deployment Summary

Current staging demo target:

```text
http://vinh-bulkybook.somee.com
```

High-level deployment flow:

1. Create Somee website.
2. Create Somee MS SQL database.
3. Apply reviewed EF migration/schema SQL script.
4. Optionally apply demo seed SQL for categories, products, cover types, and companies.
5. Publish `BulkyBookWeb`.
6. Upload the contents of the publish folder to the Somee website root.
7. Configure environment variables or protected `web.config` settings.
8. Recycle the IIS application pool / restart the Somee site.
9. Run the post-deploy smoke tests.

Important upload note: upload the contents of the publish folder, not the publish folder itself. Uploading a nested folder can cause `403` or the wrong app root.

Do not upload:

- Source folders.
- `.git/`.
- `appsettings.Development.json`.
- `App_Data/dev-mails/`.
- Local `.html` email files.
- Local publish evidence.
- SQL scripts into the web root.
- Any file containing real secrets.

Required Somee settings use placeholder values here:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<somee-ms-sql-connection-string>
Database__AutoMigrate=false
SeedData__EnableDemoData=false
Application__BaseUrl=http://vinh-bulkybook.somee.com
Stripe__EnableLocalCheckoutFallback=false
Email__Provider=Smtp
Email__Smtp__Host=smtp.gmail.com
Email__Smtp__Port=587
Email__Smtp__SecureSocketOptions=StartTls
Email__Smtp__UseStartTls=true
Email__Smtp__Username=<smtp-username>
Email__Smtp__Password=<smtp-password-or-app-password>
Email__Smtp__From=<sender-email>
```

Optional staging/demo integrations:

```text
Stripe__PublishableKey=<stripe-test-publishable-key>
Stripe__SecretKey=<stripe-test-secret-key>
Authentication__Facebook__AppId=<facebook-app-id>
Authentication__Facebook__AppSecret=<facebook-app-secret>
```

Values should be configured in the Somee panel, host environment, or protected hosting configuration depending on hosting capability. Do not write real values into committed files.

Somee deployment docs:

- [Somee Staging Deployment Runbook](docs/deployment/SOMEE_STAGING_DEPLOYMENT_RUNBOOK.md)
- [Somee Staging Smoke Test Execution](docs/deployment/SOMEE_STAGING_SMOKE_TEST_EXECUTION.md)
- [Somee Environment Variables](docs/deployment/SOMEE_ENVIRONMENT_VARIABLES.md)
- [Somee Pre-Deploy Checklist](docs/deployment/SOMEE_PRE_DEPLOY_CHECKLIST.md)
- [Somee Post-Deploy Smoke Test](docs/deployment/SOMEE_POST_DEPLOY_SMOKE_TEST.md)

## QA / Test Coverage Summary

Documented and manually tested / prepared areas include:

- Release build passes.
- Cart badge fixed to use total cart quantity instead of stale session or row count.
- Checkout recomputes `OrderTotal` server-side.
- Shipping placeholder validation prevents local/fake delivery values from being submitted silently.
- Customer `My Orders` isolation: non-staff users should see only their own orders.
- Company delayed-payment checkout sets `PaymentDueDate`.
- Real SMTP Forgot Password and Resend Email Confirmation verified locally with Gmail SMTP.
- Somee deployment smoke testing docs exist.

Do not overclaim production readiness:

- Stripe webhook/signature validation/idempotency is still a known production-hardening gap.
- Money fields still use `double` / SQL `float`; a future migration to `decimal` is recommended.
- Final production readiness requires full smoke test evidence on the deployed domain.

QA docs:

- [Runtime Manual QA Package](docs/qa/RUNTIME_MANUAL_QA_PACKAGE.md)
- [Pre-Deploy Full Test Cases](docs/qa/BULKYBOOK_PRE_DEPLOY_FULL_TEST_CASES.md)
- [Pre-Deploy Bug Register](docs/qa/BULKYBOOK_PRE_DEPLOY_BUG_REGISTER.md)
- [UI/UX Test Cases](docs/qa/BULKYBOOK_UI_UX_TEST_CASES.md)
- [Somee Deployment Test Cases](docs/qa/BULKYBOOK_SOMEE_DEPLOYMENT_TEST_CASES.md)
- [Pre-Deploy Signoff Checklist](docs/qa/BULKYBOOK_PRE_DEPLOY_SIGNOFF_CHECKLIST.md)

## Security Notes / Known Limitations

- Do not commit secrets.
- Somee demo is staging/demo only.
- Free hosting limitations apply.
- Stripe payments are demo/staging only until webhook signature validation and idempotent payment updates are implemented.
- Facebook login requires production app configuration and an approved redirect URI.
- Product uploads currently use the web server filesystem under `wwwroot/images/products`.
- Money fields currently use `double`; production financial data should use `decimal`.
- Production-grade hardening is still needed before real users or real payment processing.

## Useful Commands

Build:

```powershell
dotnet build BulkyBook.sln --configuration Release --nologo
```

Run:

```powershell
dotnet run --project BulkyBookWeb
```

List migrations:

```powershell
dotnet ef migrations list --project BulkyBook.DataAccess --startup-project BulkyBookWeb
```

Update local development database only:

```powershell
dotnet ef database update --project BulkyBook.DataAccess --startup-project BulkyBookWeb
```

Generate reviewed idempotent SQL for staging:

```powershell
dotnet ef migrations script --idempotent --project BulkyBook.DataAccess --startup-project BulkyBookWeb --output migrate-somee.sql
```

Publish locally for inspection:

```powershell
dotnet publish BulkyBookWeb/BulkyBookWeb.csproj -c Release -o publish-somee-<timestamp>
```

Run deployment readiness checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/qa/run-deployment-readiness.ps1
```

## Roadmap

Recommended future improvements:

- Add automated tests for authorization, cart ownership, order ownership, checkout behavior, upload validation, and XSS sanitization.
- Implement production-grade Stripe webhook handling with signature verification and idempotent payment updates.
- Migrate money fields from `double` to `decimal`.
- Move uploaded product images to durable external storage for real production.
- Add CI for restore/build/test.
- Capture and commit updated screenshots.
