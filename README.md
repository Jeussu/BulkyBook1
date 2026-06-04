# BulkyBook - ASP.NET Core MVC Bookstore

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![ASP.NET Core MVC](https://img.shields.io/badge/ASP.NET%20Core-MVC-512BD4)
![EF Core](https://img.shields.io/badge/EF%20Core-8.x-6DB33F)
![SQL Server](https://img.shields.io/badge/SQL%20Server-SQL%20Server-CC2927?logo=microsoftsqlserver&logoColor=white)

BulkyBook is a portfolio/demo bookstore and e-commerce web application built with ASP.NET Core MVC, Razor Views, Razor Pages Identity, Entity Framework Core, SQL Server, and Stripe Checkout integration.

The application includes a public storefront, shopping cart, checkout/order workflows, role-based admin management, Identity email flows, and Somee IIS staging/deployment support. It is intended for learning, QA practice, and portfolio review, not real production commerce.

## Project Status

| Area | Status | Notes |
| --- | --- | --- |
| Local demo | Ready | Runs on .NET 8 with SQL Server/LocalDB and local demo seed data. |
| Somee staging/demo | Deployed/prepared | Demo domain: [`https://vinh-bulkybook.somee.com`](http://vinh-bulkybook.somee.com/) / https://mybulkybook.azurewebsites.net/; publish and SQL deployment artifacts are present in the repo. |
| Production | Not production-ready | Requires payment hardening, stronger operational controls, and full production smoke evidence. |
| Stripe payments | Demo/staging only unless hardened | Normal Stripe Checkout is supported with valid keys; demo fallback is available when `Stripe__EnableLocalCheckoutFallback=true`. |

## Features

- Public storefront with book catalog browsing.
- Search by title, author, or ISBN.
- Category, cover type, and price filtering.
- Sorting and pagination.
- Product detail pages with stock state, pricing tiers, and related books.
- Shopping cart with quantity-aware cart badge.
- Individual customer checkout and order confirmation.
- Company customer delayed-payment checkout.
- Customer `My Orders` access with order ownership checks.
- Admin product, category, cover type, company, and order management.
- ASP.NET Core Identity login, registration, email confirmation, forgot password, and role-based authorization.
- SMTP email delivery for real inbox testing, including Gmail SMTP with app passwords.
- Somee/IIS deployment documentation, environment variable guidance, SQL script support, and smoke test checklists.

## Tech Stack

- .NET 8
- ASP.NET Core MVC and Razor Views
- Razor Pages for ASP.NET Core Identity UI
- Entity Framework Core 8
- SQL Server / SQL Server LocalDB / Somee MS SQL
- ASP.NET Core Identity with roles
- Repository and Unit of Work data access pattern
- Stripe.net SDK
- MailKit / MimeKit for SMTP email
- Bootstrap, jQuery, DataTables, Toastr, SweetAlert2, TinyMCE
- Somee IIS hosting

## Solution Structure

```text
BulkyBook1/
|-- BulkyBook.sln
|-- BulkyBookWeb/          ASP.NET Core MVC UI, Identity pages, controllers, views, static assets
|-- BulkyBook.Models/      Domain entities and view models
|-- BulkyBook.DataAccess/  EF Core DbContext, repositories, migrations, seeders
|-- BulkyBook.Utility/     Shared constants, email delivery, Stripe settings, helper utilities
|-- docs/                  Deployment and QA documentation
|-- tools/                 QA/deployment helper scripts
|-- DEPLOYMENT_SOMEE.md    Somee deployment notes
|-- migrate-somee.sql      Reviewed SQL deployment script
`-- README.md
```

## Local Setup

### Prerequisites

- .NET 8 SDK.
- SQL Server LocalDB or SQL Server.
- EF Core CLI tools if you plan to run local migrations.

```powershell
dotnet tool install --global dotnet-ef
dotnet tool update --global dotnet-ef
```

### Restore and Build

```powershell
dotnet restore
dotnet build BulkyBook.sln --configuration Release --nologo
```

### Configure Local Settings

Use `dotnet user-secrets` or `BulkyBookWeb/appsettings.Development.json` for local-only development values. Do not commit real passwords, SMTP app passwords, Stripe keys, Facebook secrets, or database credentials.

Example local user-secrets with placeholders:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<local-sql-connection-string>" --project BulkyBookWeb
dotnet user-secrets set "Email:Provider" "LocalFile" --project BulkyBookWeb
```

For real local email inbox testing:

```powershell
dotnet user-secrets set "Email:Provider" "Smtp" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Port" "587" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:SecureSocketOptions" "StartTls" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:UseStartTls" "true" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Username" "<smtp-username>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:Password" "<smtp-app-password>" --project BulkyBookWeb
dotnet user-secrets set "Email:Smtp:From" "<sender-email>" --project BulkyBookWeb
```

### Local Database

For local development only, apply EF Core migrations when you intentionally want to create or update your local database:

```powershell
dotnet ef database update --project BulkyBook.DataAccess --startup-project BulkyBookWeb
```

The local demo seeder includes about 20 technical book products plus categories, cover types, companies, demo users, carts, and orders.

### Run

```powershell
dotnet run --project BulkyBookWeb
```

Default local launch URLs are defined in `BulkyBookWeb/Properties/launchSettings.json`:

```text
https://localhost:7206
http://localhost:5206
```

## Somee Demo/Staging Deployment Notes

The current Somee demo target is:

```text
https://vinh-bulkybook.somee.com
```

High-level deployment flow:

1. Create the Somee website.
2. Create the Somee MS SQL database.
3. Review and apply the idempotent SQL migration script manually.
4. Publish `BulkyBookWeb`.
5. Upload the publish output contents to the Somee website root, not the parent publish folder.
6. Configure environment variables through the Somee panel or protected `web.config` values.
7. Restart/recycle the app pool.
8. Run the staging smoke tests.

Do not upload source folders, `.git/`, `appsettings.Development.json`, `App_Data/dev-mails/`, local `.html` email files, test evidence, SQL scripts, or files containing real secrets to the web root.

## Environment Variable Examples

Use placeholders only in documentation and source-controlled files:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<somee-sql-connection-string>
Database__AutoMigrate=false
SeedData__EnableDemoData=false
Application__BaseUrl=https://<somee-domain>
Stripe__EnableLocalCheckoutFallback=true
Email__Provider=Smtp
Email__Smtp__Host=smtp.gmail.com
Email__Smtp__Port=587
Email__Smtp__SecureSocketOptions=StartTls
Email__Smtp__UseStartTls=true
Email__Smtp__Username=<smtp-username>
Email__Smtp__Password=<smtp-app-password>
Email__Smtp__From=<sender-email>
```

Optional Stripe/Facebook values should also use host-level configuration or protected deployment configuration:

```text
Stripe__PublishableKey=<stripe-publishable-key>
Stripe__SecretKey=<stripe-secret-key>
Authentication__Facebook__AppId=<facebook-app-id>
Authentication__Facebook__AppSecret=<facebook-app-secret>
```

## Testing / Smoke Checklist

- Register and log in.
- Confirm email.
- Run forgot password and reset password.
- Browse, search, filter, sort, and paginate the catalog.
- Open product details and verify pricing/stock display.
- Add items to cart and verify the cart badge quantity.
- Place a demo checkout order.
- Verify `My Orders` for the customer.
- Verify admin product and order pages.
- Confirm no secrets appear in pages, logs, screenshots, publish output, or committed files.

## Known Limitations

- This is a demo/portfolio bookstore project, not production-ready commerce software.
- Demo checkout fallback is only for staging/portfolio use when real Stripe keys are not configured.
- Real Stripe production use requires valid keys, webhook signature validation, idempotency, and full payment reconciliation.
- Money fields need a future production-grade decimal/payment review before real commerce use.
- Somee free hosting can have storage, runtime, sleeping, and reliability limitations.
- Uploaded product images are stored on the web server filesystem.
- Secrets must never be committed to source control.

## Useful Docs

- [Somee Deployment Notes](DEPLOYMENT_SOMEE.md)
- [Somee Staging Deployment Runbook](docs/deployment/SOMEE_STAGING_DEPLOYMENT_RUNBOOK.md)
- [Somee Staging Smoke Test Execution](docs/deployment/SOMEE_STAGING_SMOKE_TEST_EXECUTION.md)
- [Somee Environment Variables](docs/deployment/SOMEE_ENVIRONMENT_VARIABLES.md)
- [Runtime Manual QA Package](docs/qa/RUNTIME_MANUAL_QA_PACKAGE.md)
- [SMTP Email Testing](docs/qa/SMTP_EMAIL_TESTING.md)

## Useful Commands

```powershell
dotnet build BulkyBook.sln --configuration Release --nologo
dotnet run --project BulkyBookWeb
dotnet ef migrations list --project BulkyBook.DataAccess --startup-project BulkyBookWeb
dotnet publish BulkyBookWeb/BulkyBookWeb.csproj --configuration Release --output publish-somee-<timestamp>
```

Local database update command, for local development only:

```powershell
dotnet ef database update --project BulkyBook.DataAccess --startup-project BulkyBookWeb
```
