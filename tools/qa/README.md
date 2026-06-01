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
