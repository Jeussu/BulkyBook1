# BulkyBook QA Smoke Harness

`run-smoke-tests.ps1` is a no-new-package PowerShell smoke test harness for the BulkyBook Razor app. It is intended to catch P0 startup, routing, static asset, product image, and production-copy regressions before deeper manual QA.

## How To Run

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\qa\run-smoke-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp
```

If the app is already running:

```powershell
powershell -ExecutionPolicy Bypass -File tools\qa\run-smoke-tests.ps1 -BaseUrl "https://localhost:7206" -SkipBuild
```

For more request details:

```powershell
powershell -ExecutionPolicy Bypass -File tools\qa\run-smoke-tests.ps1 -BaseUrl "https://localhost:7206" -SkipBuild -VerboseOutput
```

## Expected Local URL

The default launch profile uses:

- HTTPS: `https://localhost:7206`
- HTTP: `http://localhost:5206`

The script accepts either through `-BaseUrl`. For localhost HTTPS, it allows the development certificate during smoke requests.

## `-StartApp`

When `-StartApp` is supplied, the script starts:

```powershell
dotnet run --project BulkyBookWeb\BulkyBookWeb.csproj --no-build --urls <BaseUrl>
```

It also sets these child-process environment variables:

- `ASPNETCORE_ENVIRONMENT=Development`
- `Database__AutoMigrate=false`
- `SeedData__EnableDemoData=false`
- `SeedAdmin__Email=`
- `SeedAdmin__Password=`

This avoids running migrations and demo seeding through the smoke harness. The app's existing startup role checks may still query the database.

## What Batch B Smoke Covers

- Build unless `-SkipBuild`.
- Anonymous Home, filter, pagination, detail, login, and register URLs.
- Protected cart/admin URLs returning either 200 with an authenticated session or 302 redirect to login when anonymous.
- Rendered HTML checks for stale SVG product cover paths, modern PNG cover paths, exception page markers, and development-only user-visible text.
- 20 modern product cover PNG URLs.
- Key static assets: `/css/site.css`, `/js/product.js`, `/js/order.js`.

## Limitations

- No login automation in Batch B.
- No destructive DB writes.
- No Playwright/browser console capture.
- No DataTables AJAX verification because that requires authenticated browser/session coverage.
- No cart, checkout, product CRUD, order action, payment, upload, CSRF, IDOR, or deployment-host validation.

Use `docs\qa\MANUAL_QA_CHECKLIST.md` for the flows that require authenticated sessions, browser inspection, or local-safe reversible data.

## Customer Flow Harness

Batch C adds `run-customer-flow-tests.ps1` for the customer shopping path. It logs in with a local-safe customer account, adds a product to cart, checks cart count/cart controls, exercises Plus and Minus, verifies checkout summary fields and server-side validation, then cleans up the test-created cart item when safe.

Run with explicit credentials:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-customer-flow-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp -CustomerEmail "customer2@bulky.local" -CustomerPassword "Customer123!" -ProductId 1
```

If credentials are omitted, the script attempts to read local development customer seed credentials from `DemoDataSeeder.cs`. It does not create users and does not run migrations.

Optional local-safe order placement:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-customer-flow-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp -CustomerEmail "customer2@bulky.local" -CustomerPassword "Customer123!" -ProductId 1 -PlaceOrder
```

Use `-PlaceOrder` only against disposable local data and known payment configuration. Without `-PlaceOrder`, the script does not intentionally create orders.

## Admin Flow Harness

Batch D adds `run-admin-flow-tests.ps1` for authenticated admin smoke coverage. It logs in with a local-safe admin account, checks protected admin routes, verifies Product/Company/Order DataTables endpoints and selectors, inspects Product Upsert and Order Details bindings, and checks Category/CoverType/Company admin screens without mutating data by default.

Run the default non-destructive pass:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-admin-flow-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp -AdminEmail "admin@bulky.local" -AdminPassword "Admin123!" -ProductId 1 -OrderId 1
```

If credentials are omitted, the script attempts to read the local development `SeedAdmin` account from `BulkyBookWeb\appsettings.Development.json`, then the demo seed file. It does not create users, run migrations, or seed demo data.

Optional CRUD/destructive modes are intentionally gated:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-admin-flow-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp -RunCrud
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-admin-flow-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp -RunCrud -RunDestructive -CleanupTestData
```

Use those flags only against disposable local data. The default run does not create products, delete records, ship orders, cancel orders, or perform payment actions.

## Security Harness

Batch E adds `run-security-tests.ps1` for security regression coverage. It performs static checks for anti-forgery, unsafe GET mutation candidates, role gates, sanitized `Html.Raw` usage, upload validation, AJAX CSRF headers, and dynamic checks for anonymous/customer/admin access boundaries.

Run the default security pass:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-security-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp -CustomerEmail "customer2@bulky.local" -CustomerPassword "Customer123!" -AdminEmail "admin@bulky.local" -AdminPassword "Admin123!" -ProductId 1 -OrderId 1
```

Optional local-safe checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-security-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp -CustomerEmail "customer2@bulky.local" -CustomerPassword "Customer123!" -AdminEmail "admin@bulky.local" -AdminPassword "Admin123!" -ProductId 1 -OrderId 1 -RunUploadTests -RunOverpostingTests -RunIdorTests
```

The optional upload check posts an invalid `.txt` file and expects server-side rejection without creating a product. Over-posting and cross-customer IDOR checks are intentionally conservative and skip when disposable local data is not available.

## Deployment Readiness Harness

Batch F adds `run-deployment-readiness.ps1` for IIS/Somee-style publish validation. It publishes the web project to a unique temp folder by default, verifies expected publish files/static assets, confirms `appsettings.Development.json` is excluded, checks production-safe config defaults, and scans text publish artifacts for obvious local/demo credentials or secrets.

Run the default publish validation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-deployment-readiness.ps1
```

Validate an existing publish folder without publishing again:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-deployment-readiness.ps1 -SkipPublish -PublishDir "C:\path\to\publish"
```

The default temp publish output is disposable. Do not commit publish output or generated deployment SQL scripts.

## Final Staging Dry Run Helpers

Batch G uses a fixed local publish folder for final upload review:

```powershell
dotnet publish BulkyBookWeb\BulkyBookWeb.csproj -c Release -o publish-somee-final
powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-deployment-readiness.ps1 -SkipPublish -PublishDir "publish-somee-final"
```

Generate the review-only Somee SQL migration script with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\deployment\generate-somee-migration-script.ps1
```

The helper writes `migrate-somee.sql` and does not apply it. Review the SQL before using Somee SQL tools or SSMS, and do not commit publish output or generated SQL unless explicitly requested.
