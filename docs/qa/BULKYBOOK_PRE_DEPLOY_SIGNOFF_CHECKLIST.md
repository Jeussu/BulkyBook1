# BulkyBook Pre-Deploy Signoff Checklist

Use this checklist after all P0 test cases are executed. Do not approve deployment while any P0 item is open.

## Build and Source

- [ ] `git branch --show-current` captured.
- [ ] `git status --short` reviewed; no unexpected changes.
- [ ] `dotnet build BulkyBook.sln --configuration Release --nologo` succeeds.
- [ ] `git diff --check` passes.
- [ ] No migration was created in this pass.
- [ ] No `dotnet ef database update` was run in this pass.
- [ ] No DB reset/delete/reseed was run in this pass.

## Secret Hygiene

- [ ] No connection string, Stripe key, SMTP password, Gmail app password, Facebook secret, or user password appears in source/docs/screenshots.
- [ ] Local secrets are stored with `dotnet user-secrets`.
- [ ] Somee/staging/production secrets are stored as environment variables.
- [ ] Publish output was scanned for secrets.

## Identity Email

- [ ] `/Admin/Diagnostics/Email` in Development shows `Provider=Smtp` for real inbox testing.
- [ ] Forgot Password sends a real email for a confirmed user.
- [ ] Resend Email Confirmation sends a real email for an unconfirmed user.
- [ ] Unknown/already-confirmed/unconfirmed branches remain generic and do not enumerate accounts.
- [ ] `RegisterConfirmation` does not show a direct token link outside Development `LocalFile` mode.
- [ ] LocalFile support still works for local-only testing when explicitly configured.

## Cart and Checkout

- [ ] Cart badge equals total item quantity.
- [ ] Cart update/remove/empty actions update UI and DB consistently.
- [ ] Individual checkout succeeds with valid Stripe test config or Development fallback.
- [ ] Individual checkout blocks cleanly when Stripe is missing and fallback disabled.
- [ ] Company delayed-payment checkout succeeds and sets `PaymentDueDate`.
- [ ] Posted `OrderHeader.OrderTotal` cannot affect stored order total.
- [ ] Placeholder profile values cannot be silently submitted as shipping details.

## Orders and Authorization

- [ ] Customers can open `My Orders`.
- [ ] Customers can only see their own orders.
- [ ] Employees can manage orders according to role rules.
- [ ] Admins can manage master data and orders.
- [ ] Anonymous users cannot access protected cart/checkout/admin actions.

## UI/UX

- [ ] Home, Details, Cart, Summary, My Orders, Admin Orders, and Identity pages are usable on desktop.
- [ ] Same pages are usable on mobile width.
- [ ] Validation and error messages are visible and actionable.
- [ ] No broad redesign was introduced in this pass.

## Somee Readiness

- [ ] `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] `ConnectionStrings__DefaultConnection` points to intended Somee DB.
- [ ] `Database__AutoMigrate=false`.
- [ ] `SeedData__EnableDemoData=false`.
- [ ] `Application__BaseUrl` matches final HTTPS Somee URL.
- [ ] `Email__Provider=Smtp` and SMTP settings are configured.
- [ ] `Stripe__EnableLocalCheckoutFallback=false`.
- [ ] Stripe test/live keys are configured only if checkout payment is expected.

## Open Risks

- [ ] Stripe webhook/idempotency risk accepted or scheduled.
- [ ] `double`/SQL `float` money model risk accepted or scheduled for schema migration.
- [ ] Identity nullable/analyzer warnings accepted or scheduled.

## Decision

- [ ] Approved for staging deploy.
- [ ] Approved for production deploy.
- [ ] Blocked. Blocking IDs: `________________`.

Approver:

Date:
