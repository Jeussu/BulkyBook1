# Final Staging Dry Run Report

Date: 2026-06-01

## Scope

Final local verification before uploading the BulkyBook ASP.NET Core MVC app to a Somee/IIS-style staging host.

This pass did not create migrations, did not run `update-database`, did not apply SQL to any database, and did not change application code.

## Repository State

| Item | Result |
|---|---|
| Branch | `ui/stitch-modern-refresh` |
| Baseline commit before Batch G | `54e376b` |
| Initial working tree | Clean |
| App files changed | None |
| Publish output committed? | No; `publish-somee-final/` is ignored |
| SQL script committed? | No; `migrate-somee.sql` is ignored |

## Build Results

| Command | Result |
|---|---|
| `dotnet build` | Passed, 0 warnings, 0 errors after stopping a stale local `BulkyBookWeb.exe` that was locking build output |
| `dotnet build --no-incremental` | Passed, 58 existing warnings, 0 errors |

The no-incremental warnings are existing nullable/analyzer warnings from the codebase, not new Batch G changes.

## Regression Harness Results

| Harness | Command summary | Result |
|---|---|---|
| Smoke | `tools\qa\run-smoke-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp` | 66 passed, 0 failed |
| Customer flow | `tools\qa\run-customer-flow-tests.ps1 ... -CustomerEmail "customer2@bulky.local" ... -ProductId 1` | 40 passed, 0 failed |
| Admin flow | `tools\qa\run-admin-flow-tests.ps1 ... -AdminEmail "admin@bulky.local" ... -ProductId 1 -OrderId 1` | 102 passed, 0 failed |
| Security | `tools\qa\run-security-tests.ps1 ... -CustomerEmail ... -AdminEmail ... -ProductId 1 -OrderId 1` | 68 passed, 0 failed |
| Deployment readiness | `tools\qa\run-deployment-readiness.ps1` | 26 passed, 0 failed |

The customer flow created local-safe cart item `9019` during the test and cleaned it up.

## Publish Output

| Item | Result |
|---|---|
| Publish command | `dotnet publish BulkyBookWeb\BulkyBookWeb.csproj -c Release -o publish-somee-final` |
| Publish result | Passed |
| Publish output path | `D:\Web API\BlueVilla\BulkyBook1\publish-somee-final` |
| Publish validation command | `tools\qa\run-deployment-readiness.ps1 -SkipPublish -PublishDir "publish-somee-final"` |
| Publish validation result | 26 passed, 0 failed |

Publish validation confirmed:

- `web.config` exists.
- `appsettings.Development.json` is absent.
- `appsettings.Production.json` exists.
- `appsettings.json` does not contain local fallback secrets.
- `site.css`, `product.js`, `order.js`, and `company.js` exist.
- 20 modern PNG book cover assets are included.
- No LocalDB connection string was detected in publish text artifacts.
- No Stripe, Facebook, or SMTP secrets were detected in publish text artifacts.
- No demo admin/customer credentials were detected in publish text artifacts.

## Migration SQL

Generated for review only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\deployment\generate-somee-migration-script.ps1
```

Output:

```text
D:\Web API\BlueVilla\BulkyBook1\migrate-somee.sql
```

This script is idempotent and intended for manual review/application through Somee SQL tools or SSMS. It was not applied to any database.

## Somee Readiness Checklist

- Upload the contents of `publish-somee-final`, not the folder itself.
- Do not upload source folders, `.git`, `bin`, `obj`, docs, QA scripts, screenshots, or local logs.
- Do not upload `appsettings.Development.json`.
- Configure production environment variables from `docs\deployment\SOMEE_ENVIRONMENT_VARIABLES.md`.
- Keep `Database__AutoMigrate=false`.
- Keep `SeedData__EnableDemoData=false`.
- Apply reviewed SQL through Somee SQL tools only if the target database needs it.
- Verify `wwwroot/images/products` write permission before testing admin product image uploads.
- Verify static files after upload: `/css/site.css`, `/js/product.js`, `/js/order.js`, `/js/company.js`, and modern cover PNG URLs.
- Test anonymous Home/Login/Register before authenticated admin/customer flows.
- Test payments only with approved Stripe test configuration.

## Environment Variables

Required placeholders are documented in `docs\deployment\SOMEE_ENVIRONMENT_VARIABLES.md`:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ConnectionStrings__DefaultConnection`
- `Database__AutoMigrate=false`
- `SeedData__EnableDemoData=false`
- `Application__BaseUrl`
- `Stripe__EnableLocalCheckoutFallback=false`

Optional Stripe, Facebook, and SMTP values should be set only through the host, never committed files.

## Blockers

No publish, harness, or deployment-readiness blocker was found in Batch G.

## Recommendation

The publish package is ready for Somee staging upload, subject to host-level environment variable configuration, reviewed SQL application, and a post-upload smoke pass.
