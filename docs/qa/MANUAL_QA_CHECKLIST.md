# BulkyBook Manual QA Checklist

Use this checklist after running `tools\qa\run-smoke-tests.ps1`. Keep test data reversible. Do not run destructive admin/order actions against shared or production data.

## Pre-Test Setup

- Confirm branch is `ui/stitch-modern-refresh`.
- Confirm `git status --short` is clean or only contains the QA changes currently under review.
- Confirm `dotnet build` succeeds.
- Start the app locally with `Database__AutoMigrate=false` unless intentionally testing migrations.
- Use a local-safe database snapshot or disposable data for create/update/delete tests.
- Confirm browser dev tools console is open for manual UI checks.

## Anonymous Smoke

- Open `/Customer/Home/Index`.
- Search with a common term such as `core`.
- Apply category, cover type, price, sort, page size, and pagination.
- Open `/Customer/Home/Details?productId=1`.
- Open `/Identity/Account/Login`.
- Open `/Identity/Account/Register`.
- Confirm protected cart/admin URLs redirect to login when anonymous.
- Confirm no user-visible local/demo/test or raw configuration guidance appears.

## Customer Smoke

- Run the customer flow harness first when local-safe credentials are available:
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\run-customer-flow-tests.ps1 -BaseUrl "https://localhost:7206" -StartApp -StopApp -CustomerEmail "customer2@bulky.local" -CustomerPassword "Customer123!" -ProductId 1`
- Login with a local-safe customer account.
- Open product detail, change quantity, and add to cart.
- Verify cart count changes.
- Open `/Customer/Cart/Index`.
- Test Plus, Minus, Remove, and empty cart behavior only on disposable cart data.
- Open `/Customer/Cart/Summary` with items.
- Verify shipping validation and order summary totals.
- Place order only in local-safe data and known payment configuration.
- Verify order confirmation displays the correct order number.
- Logout and confirm nav state resets.

## Admin Smoke

- Login with a local-safe admin account.
- Open `/Admin/Product/Index`; verify DataTables load, search, sort, and paginate.
- Open `/Admin/Product/Upsert` and `/Admin/Product/Upsert?id=1`.
- Verify required fields, category/cover selects, `#uploadBox`, image preview, and validation.
- Open `/Admin/Order/Index`; verify status filters and DataTables load.
- Open `/Admin/Order/Details?orderId=1`.
- Verify customer fields, payment/status metadata, item list, totals, and action buttons.
- Do not ship/cancel/delete unless the data is disposable and the action is intentionally tested.

## UI Responsive Pass

- Check 1440px, 1280px, 768px, and 390px widths.
- Pages: Home, Details, Cart, Summary, Login, Register, Product List, Product Upsert, Order List, Order Details.
- Confirm no major horizontal overflow.
- Confirm navbar, dropdowns, forms, tables, cards, badges, pagination, and footer remain usable.
- Confirm text is readable and buttons remain tappable on mobile.

## Security Sanity Pass

- Confirm POST forms include anti-forgery tokens.
- Confirm destructive actions are not exposed as simple unauthenticated GET actions.
- Confirm customers cannot access admin pages.
- Confirm customer-owned cart/order data cannot be accessed by another customer.
- Confirm product description HTML behavior is understood and safe.
- Confirm file upload rejects unsafe file names/types in local-safe admin tests.
- Confirm rendered HTML does not expose secrets or development configuration instructions.

## Deployment Preflight

- Confirm appsettings values required for production are supplied by the host/environment.
- Confirm Stripe/Facebook/email keys are absent from source and publish output unless intentionally configured through the host.
- Confirm static assets return 200 after publish.
- Confirm no stale SVG product cover paths are rendered.
- Confirm `Database:AutoMigrate` policy is explicit for the deployment target.

## Post-Fix Regression

- Re-run `dotnet build`.
- Re-run `tools\qa\run-smoke-tests.ps1`.
- Re-test the exact flow that failed.
- Re-test adjacent pages sharing the same layout/component.
- Capture before/after screenshots for UI fixes.

## Screenshots To Capture

- Home/catalog desktop and mobile.
- Product detail desktop and mobile.
- Cart and checkout summary.
- Login/Register mobile.
- Admin product list and product upsert.
- Admin order details.

## Do Not Test Destructively

- Do not run migrations against shared/prod data.
- Do not delete real products, categories, cover types, companies, or users.
- Do not cancel/ship/refund real orders.
- Do not upload untrusted files outside a local-safe test environment.
- Do not place real payments unless using an approved test Stripe configuration.
