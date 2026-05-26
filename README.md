# BulkyBook - ASP.NET Core MVC Bookstore

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![ASP.NET Core MVC](https://img.shields.io/badge/ASP.NET%20Core-MVC-512BD4)
![EF Core](https://img.shields.io/badge/EF%20Core-8.x-6DB33F)
![SQL Server](https://img.shields.io/badge/SQL%20Server-EF%20Core%20Provider-CC2927?logo=microsoftsqlserver&logoColor=white)

BulkyBook is a technical bookstore / e-commerce demo built with ASP.NET Core MVC, Razor Views, ASP.NET Core Identity, Entity Framework Core, and SQL Server. The project started from a classic BulkyBook-style learning app and has been modernized for local portfolio review, demo data testing, and future staging deployment planning.

The current branch focuses on a realistic local bookstore workflow: public catalog browsing, role-based administration, shopping cart, checkout, company delayed payment, order management, seeded demo accounts, bookstore UI polish, and Somee/IIS deployment preparation.

## Project Status

| Area | Status | Notes |
| --- | --- | --- |
| Local demo | Ready | Runs locally on .NET 8 with SQL Server LocalDB and seeded demo data. |
| Portfolio review | Ready | Suitable for local walkthroughs and screenshots after final visual review. |
| Production deployment | Not fully ready | Deployment preparation exists, but real production hardening remains. |
| Stripe payments | Demo/staging only | Stripe SDK integration exists, but webhook signature validation and idempotency are not production-ready yet. |
| Facebook login | Implemented but not configured | Requires production AppId/AppSecret and a valid HTTPS redirect URI. |
| Somee staging | Prepared, not deployed | See [DEPLOYMENT_SOMEE.md](DEPLOYMENT_SOMEE.md). |

## Features

### Public Storefront

- Book catalog homepage.
- Search by title, author, or ISBN.
- Filter by category, cover type, and price range.
- Sort by newest, title, and price.
- Pagination with filter preservation.
- Product detail page with cover, pricing tiers, stock status, description, and related books.
- Local SVG technical book covers for demo products.

### Customer

- Register, login, logout, and Identity account management.
- Shopping cart with add, plus, minus, and remove actions.
- Anti-forgery-protected cart mutations.
- Checkout summary.
- Individual checkout flow with Stripe integration or local development fallback.
- Order confirmation.
- "My Orders" behavior through the order management page for non-staff users.

### Company User

- Company-linked user accounts.
- Company cart and checkout flow.
- Delayed payment order status support.
- Company order history through ownership-limited order views.

### Admin

- Category management.
- Cover type management.
- Product management.
- Product image upload with validation.
- Stock quantity and active/inactive product status.
- Company management.
- Order list, filters, details, processing, shipping, cancellation, and refund flow.

### Employee

- Limited order management access.
- Can work with order processing workflows without full catalog administration privileges.

### Security and Quality Improvements

- Role-based authorization for admin/customer flows.
- Cart and order ownership checks to reduce IDOR risk.
- POST + anti-forgery protection for state-changing cart actions.
- Public registration role escalation protection.
- Product upload validation for extension, content type, file size, and server-side filenames.
- Basic HTML sanitization for product descriptions before rendering.
- Delete restrictions to protect historical order data.
- Development-only demo seeding controlled by configuration.
- Production/staging configuration placeholders without real secrets.

## Screenshots

Screenshots will be added after final UI review.

Suggested screenshot set:

- Home/catalog with search and filters.
- Product detail page.
- Shopping cart and checkout summary.
- Admin order detail page.
- Product management page.

## Tech Stack

- .NET 8
- ASP.NET Core MVC
- Razor Views
- Razor Pages for ASP.NET Core Identity UI
- Entity Framework Core 8
- SQL Server / SQL Server LocalDB
- ASP.NET Core Identity with roles
- Bootstrap / Bootswatch
- jQuery
- DataTables
- Toastr
- SweetAlert2
- TinyMCE for product description editing
- Stripe.net SDK
- Facebook OAuth provider
- MailKit / MimeKit for email sending

## Solution Structure

```text
BulkyBook1/
|-- BulkyBook.sln
|-- BulkyBookWeb/
|   |-- Areas/
|   |   |-- Admin/
|   |   |-- Customer/
|   |   `-- Identity/
|   |-- ViewComponents/
|   |-- Views/
|   |-- wwwroot/
|   |   |-- css/
|   |   |-- js/
|   |   `-- images/products/book-covers/
|   |-- Program.cs
|   `-- appsettings*.json
|-- BulkyBook.DataAccess/
|   |-- Data/
|   |-- DbInitializer/
|   |-- Migrations/
|   `-- Repository/
|-- BulkyBook.Models/
|   `-- ViewModels/
|-- BulkyBook.Utility/
`-- DEPLOYMENT_SOMEE.md
```

### Projects

| Project | Purpose |
| --- | --- |
| `BulkyBookWeb` | ASP.NET Core MVC web app, Razor views, Identity pages, controllers, static assets, startup configuration. |
| `BulkyBook.DataAccess` | EF Core `ApplicationDbContext`, migrations, repositories, unit of work, database initializer, demo data seeder. |
| `BulkyBook.Models` | Domain entities and view models. |
| `BulkyBook.Utility` | Constants, Stripe settings, email sender, and small shared helpers. |

### Main Areas

- `Areas/Admin`: category, cover type, product, company, and order management.
- `Areas/Customer`: public catalog, product detail, cart, checkout, and order confirmation.
- `Areas/Identity`: scaffolded and customized ASP.NET Core Identity pages.
- `DataAccess/Repository`: repository and unit-of-work pattern.
- `DbInitializer`: role/admin seed and local demo data seed.
- `wwwroot/images/products/book-covers`: local SVG covers for seeded demo books.

## Local Development Setup

### Prerequisites

- .NET 8 SDK
- SQL Server LocalDB or SQL Server
- Visual Studio 2022 or another .NET-compatible IDE
- EF Core CLI tools

If EF tools are not installed:

```powershell
dotnet tool install --global dotnet-ef
```

Or update an existing install:

```powershell
dotnet tool update --global dotnet-ef
```

### Run Locally

```powershell
git clone https://github.com/Jeussu/BulkyBook1.git
cd BulkyBook1
dotnet restore BulkyBook.sln
dotnet build BulkyBook.sln
dotnet ef database update --project BulkyBook.DataAccess --startup-project BulkyBookWeb --context ApplicationDbContext
dotnet run --project BulkyBookWeb --launch-profile BulkyBookWeb
```

Open:

```text
https://localhost:7206
```

Alternative HTTP profile:

```text
http://localhost:5206
```

## Local Configuration

Local development uses `BulkyBookWeb/appsettings.Development.json`.

Default local database:

```text
BulkyBook_Local
```

Development behavior:

- `Database:AutoMigrate=true`
- `SeedData:EnableDemoData=true`
- `Stripe:EnableLocalCheckoutFallback=true`
- Facebook login is disabled unless AppId/AppSecret are configured.
- Email sending is a safe no-op unless SMTP settings are configured.

Do not put production secrets in `appsettings.json` or `appsettings.Development.json`. Use environment variables, user secrets, or hosting-provider configuration for real credentials.

## Demo Accounts

These accounts are for local development and portfolio review only.

| Role | Email | Password | Purpose |
| --- | --- | --- | --- |
| Admin | `admin@bulky.local` | `Admin123!` | Full catalog, company, and order management. |
| Employee | `employee@bulky.local` | `Employee123!` | Limited order management. |
| Customer | `customer@bulky.local` | `Customer123!` | Individual shopping, cart, checkout, and orders. |
| Customer | `customer2@bulky.local` | `Customer123!` | Additional customer for ownership testing. |
| Company | `company@bulky.local` | `Company123!` | Company checkout and delayed payment flow. |

Do not reuse these credentials in any deployed environment.

## Database and Seed Data

The app uses EF Core migrations with SQL Server.

Current notable schema additions include:

- `Product.StockQuantity`
- `Product.IsActive`
- delete restrictions for product/order/user relationship safety

The demo seeder creates local test data for:

- roles
- users
- companies
- categories
- cover types
- products/books
- shopping carts
- order headers
- order details

Demo data is idempotent and gated by Development environment plus `SeedData:EnableDemoData`.

## Configuration Keys

Important configuration keys:

```text
ConnectionStrings__DefaultConnection
Database__AutoMigrate
Application__BaseUrl
SeedData__EnableDemoData
Stripe__PublishableKey
Stripe__SecretKey
Stripe__WebhookSecret
Stripe__EnableLocalCheckoutFallback
Authentication__Facebook__AppId
Authentication__Facebook__AppSecret
Email__Smtp__Host
Email__Smtp__Port
Email__Smtp__Username
Email__Smtp__Password
Email__Smtp__From
```

Production/staging should use external configuration. Real secrets must never be committed.

## Deployment Preparation

Somee Free ASP.NET Hosting / Windows IIS / MS SQL Express preparation is documented in:

[DEPLOYMENT_SOMEE.md](DEPLOYMENT_SOMEE.md)

The project has been prepared for manual staging deployment, but deployment has not been performed from this branch.

Recommended pre-publish checks:

```powershell
dotnet restore BulkyBook.sln
dotnet build BulkyBook.sln
dotnet ef migrations list --project BulkyBook.DataAccess --startup-project BulkyBookWeb --context ApplicationDbContext
dotnet publish BulkyBookWeb -c Release -o ./publish-somee
```

Production/staging database updates should use a reviewed idempotent migration script:

```powershell
dotnet ef migrations script --project BulkyBook.DataAccess --startup-project BulkyBookWeb --context ApplicationDbContext --idempotent -o migrate-somee.sql
```

Do not commit publish output or generated SQL scripts unless intentionally requested.

## Stripe and Facebook Notes

### Stripe

Stripe checkout code exists for individual payment flows and admin payment actions. Local development can use a fallback mode when Stripe keys are missing.

Not production-ready yet:

- no production webhook endpoint
- no Stripe signature verification
- payment callback trust and idempotency need hardening
- refund/payment state handling needs deeper production QA

Use Stripe test keys only for staging/demo.

### Facebook Login

Facebook OAuth provider registration is conditional. It only activates when both AppId and AppSecret are configured.

Production/staging requires:

```text
https://<your-domain>/signin-facebook
```

as a valid OAuth redirect URI in the Facebook Developer Console.

## Known Limitations

- Real Stripe payments are not production-ready.
- Facebook login requires production OAuth configuration.
- Money fields still use `double`; a future migration to `decimal(18,2)` is recommended.
- Product uploads currently use the web server filesystem under `wwwroot/images/products`.
- Automated test coverage is limited or not yet established.
- Release/publish hardening may still require nullable and scaffold cleanup.
- The current deployment target is staging/portfolio first, not real paid production.

## Useful Commands

Build:

```powershell
dotnet build BulkyBook.sln
```

Run:

```powershell
dotnet run --project BulkyBookWeb --launch-profile BulkyBookWeb
```

List migrations:

```powershell
dotnet ef migrations list --project BulkyBook.DataAccess --startup-project BulkyBookWeb --context ApplicationDbContext
```

Update local database:

```powershell
dotnet ef database update --project BulkyBook.DataAccess --startup-project BulkyBookWeb --context ApplicationDbContext
```

Publish locally for inspection:

```powershell
dotnet publish BulkyBookWeb -c Release -o ./publish-somee
```

## Roadmap

Recommended next improvements:

- Add automated tests for authorization, cart ownership, order ownership, upload validation, XSS sanitization, and checkout behavior.
- Implement production-grade Stripe webhook handling with signature verification and idempotent payment updates.
- Decide whether Facebook login should remain enabled for production.
- Migrate money fields from `double` to `decimal`.
- Move uploaded product images to durable external storage for real production.
- Add CI for restore/build/test.
- Capture updated screenshots for this README.
