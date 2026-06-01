# BulkyBook QA Batch Plan

This plan separates test foundation from higher-risk behavior fixes. Batch B creates coverage and identifies issues; later batches may fix deeper bugs within explicitly approved scopes.

## Batch B: QA Foundation + P0 Smoke

| Field | Plan |
|---|---|
| Goal | Create project-wide QA backlog, manual checklist, batch plan, and a no-new-package smoke harness; run P0 anonymous/static smoke checks. |
| Allowed files | `docs/qa/*`, `tools/qa/*`; only small UI fixes in shared notification/login/layout, Home Index, and `site.css` if P0 smoke finds them. |
| Disallowed files | Controllers, models, viewmodels, repositories, DbContext, migrations, Program.cs, appsettings, seeders, product images, business logic. |
| Test cases covered | A-001, A-002, A-006, A-007, A-008, A-009, A-010, A-012, D-002, D-003, F-006, F-012, F-014, G-005. |
| Bug fix scope | Smoke script false positives, documentation gaps, remaining user-visible local/demo/test text in allowed UI files, notification rendering, Home visual regressions. |
| Rollback strategy | Revert docs/tools additions and any explicitly scoped UI fix. No DB rollback should be required. |
| Migration allowed? | No. |
| DB data update allowed? | No intentional DB writes. Starting the app may run existing startup role checks; do not seed demo data in the smoke harness. |
| Risk level | Low. |

## Batch C: Customer Cart / Checkout / Order Data Integrity Test-Fix

| Field | Plan |
|---|---|
| Goal | Validate and fix customer shopping, cart, checkout, confirmation, totals, cart count, and payment fallback behavior. |
| Allowed files | Customer Home/Cart views, cart/order-related controllers only after explicit approval, `site.css`, targeted tests/docs. |
| Disallowed files | Admin CRUD, Identity PageModel code unless directly blocking customer flow, migrations/schema changes unless separately approved. |
| Test cases covered | B-001 through B-014, D-004, D-005, D-006, D-007, F-001 through F-009, F-014. |
| Bug fix scope | Model binding preservation, cart quantity actions, checkout validation, order creation consistency, safe payment fallback messaging. |
| Rollback strategy | Commit small fixes per flow; revert individual flow commits if totals/payment/cart behavior regresses. |
| Migration allowed? | No by default. |
| DB data update allowed? | Local-safe reversible test data only. |
| Risk level | Medium-high because cart/order/payment behavior is business critical. |

## Batch D: Admin CRUD / DataTables / Order Management Test-Fix

| Field | Plan |
|---|---|
| Goal | Validate and fix admin product/category/cover/company CRUD, DataTables behavior, product upload, and order management UI/actions. |
| Allowed files | Admin Razor views, `wwwroot/js/product.js`, `wwwroot/js/order.js`, admin controllers only after explicit approval, targeted CSS/tests/docs. |
| Disallowed files | Customer checkout/payment logic except where order admin action requires an approved scoped fix; migrations/schema unless separately approved. |
| Test cases covered | C-001 through C-014, D-008, D-009, F-010, F-011. |
| Bug fix scope | DataTables selector/endpoints, admin form binding, upload validation UI, order status/action rendering, reversible CRUD defects. |
| Rollback strategy | Commit per admin area; keep destructive tests on disposable data; restore DB snapshot after delete/action tests. |
| Migration allowed? | No by default. |
| DB data update allowed? | Yes, local-safe reversible CRUD data only. |
| Risk level | Medium-high because admin actions mutate catalog/order state. |

## Batch E: Security Hardening Test-Fix

| Field | Plan |
|---|---|
| Goal | Validate CSRF, authorization, IDOR, upload safety, XSS, over-posting, and secret leakage; fix approved security defects. |
| Allowed files | Controllers, Razor forms, validation helpers, upload handling, authorization attributes, security docs/tests after explicit approval. |
| Disallowed files | Cosmetic-only UI changes, unrelated refactors, schema changes unless required and approved. |
| Test cases covered | E-001 through E-012, C-015, G-007. |
| Bug fix scope | High-confidence security defects with focused tests and no broad behavior rewrite. |
| Rollback strategy | Commit one security fix at a time; document exploit/regression test; revert if authorization or binding behavior breaks legitimate flows. |
| Migration allowed? | Only if a security fix explicitly requires it and the user approves. |
| DB data update allowed? | Controlled local-safe data only. |
| Risk level | High because auth/payment/data access behavior may change. |

## Batch F: Deployment Readiness + Publish Package Test-Fix

| Field | Plan |
|---|---|
| Goal | Validate production config, publish output, static assets, hosting startup, deployment docs, and no development-only user-visible text. |
| Allowed files | Deployment docs, publish scripts/profiles, configuration documentation, targeted startup/config fixes after approval. |
| Disallowed files | Business logic, schema changes, broad UI redesign, destructive production data changes. |
| Test cases covered | G-001 through G-008, D-010, selected deployment variants of A-012 and G-005. |
| Bug fix scope | Publish packaging, config safety, host-specific startup/static asset issues, deployment checklist corrections. |
| Rollback strategy | Keep deployment changes isolated; restore previous publish profile/package; never apply irreversible production DB changes. |
| Migration allowed? | Only through an approved deployment migration plan. |
| DB data update allowed? | No production data mutation during readiness smoke. |
| Risk level | Medium-high because environment differences can expose startup/config issues. |

## Batch G: Final Staging Dry Run + Visual QA

| Field | Plan |
|---|---|
| Goal | Re-run full local regressions, generate final publish output, validate Somee/IIS package readiness, generate review-only SQL, and perform final visual QA before staging upload. |
| Allowed files | Final deployment/QA docs, Somee checklist/env docs, migration-script helper, `.gitignore` for generated publish/sql artifacts, and only small proven visual/deployment app fixes. |
| Disallowed files | Controllers, models, repositories, DbContext, migrations, payment/Identity/business logic, product images unless a missing/broken publish asset is proven. |
| Test cases covered | Full smoke/customer/admin/security/deployment harnesses plus final visual widths at 1440px, 1280px, 768px, and 390px. |
| Bug fix scope | Only high-confidence staging blockers or small visual/deployment regressions; larger visual polish is deferred. |
| Rollback strategy | Remove final docs/helper scripts or revert isolated deployment-only changes; never roll forward schema through the app. |
| Migration allowed? | No. Only generate idempotent SQL for review. |
| DB data update allowed? | Local-safe reversible cart data only through harness/visual QA; no production/shared data mutation. |
| Risk level | Medium because it validates deployment packaging and host readiness without changing backend behavior. |

## Deferred Issue Register

Use this section after each batch to record deeper findings discovered outside the current allowed scope.

| Issue ID | Found in batch | Summary | Evidence | Assigned batch | Suggested files | Status |
|---|---|---|---|---|---|---|
| DEF-001 | B | No deeper issue recorded yet. Update this table if Batch B smoke finds a business/security/data defect outside its allowed scope. | N/A | C/D/E/F | N/A | Placeholder |
| DEF-002 | C | Customer flow harness now covers invalid checkout summary submissions; keep this in future regressions because the checkout action previously needed explicit server-side `ModelState` validation before order creation. | `CartController.SummaryPOST`, `run-customer-flow-tests.ps1` | C regression | `BulkyBookWeb/Areas/Customer/Controllers/CartController.cs`, `tools/qa/run-customer-flow-tests.ps1` | Fixed in Batch C; retain regression coverage |
| DEF-003 | D | `ShipOrder` previously relied on client-side `validateInput()` for carrier/tracking only; direct POST now has server-side validation before changing order status. | `OrderController.ShipOrder`, `Order/Details.cshtml`, `run-admin-flow-tests.ps1` | D regression | `BulkyBookWeb/Areas/Admin/Controllers/OrderController.cs`, `BulkyBookWeb/Areas/Admin/Views/Order/Details.cshtml`, `tools/qa/run-admin-flow-tests.ps1` | Fixed in Batch D; destructive status-transition execution remains manual/local-only |
| DEF-004 | E | Customer users could reach the Admin Order list/API at `/Admin/Order/Index` and `/Admin/Order/GetAll?status=all`; the API scoped rows by owner, but the Admin management surface should remain staff-only. | `run-security-tests.ps1` observed HTTP 200 for customer on both routes before fix. | E regression | `BulkyBookWeb/Areas/Admin/Controllers/OrderController.cs`, `tools/qa/run-security-tests.ps1` | Fixed in Batch E by adding Admin/Employee role gates to `Index` and `GetAll` |
| DEF-005 | E | `CartController.OrderConfirmation` and `OrderController.PaymentConfirmation` are GET endpoints with callback/confirmation side effects. They are tied to checkout/payment redirect behavior and should be reviewed with a payment callback design before route conversion. | Static security scan in `run-security-tests.ps1` flags both as GET mutation candidates. | Future security/payment hardening | `BulkyBookWeb/Areas/Customer/Controllers/CartController.cs`, `BulkyBookWeb/Areas/Admin/Controllers/OrderController.cs` | Deferred; not changed in Batch E to avoid breaking checkout/Stripe redirect semantics |
| DEF-006 | F | Base `appsettings.json` still carried local fallback copy (`no-reply@localhost`, `Local Admin`, local address defaults) that could appear in publish artifacts even though production overrides were safe. | Batch F publish leakage scan found local fallback strings in published `appsettings.json`. | F regression | `BulkyBookWeb/appsettings.json`, `tools/qa/run-deployment-readiness.ps1`, `DEPLOYMENT_SOMEE.md` | Fixed in Batch F by neutralizing base config fallback values and adding publish leakage validation |
| DEF-007 | G | Admin DataTables at 390px rely on horizontal scrolling and can show clipped columns in the first mobile viewport. | Batch G visual screenshot `admin-390-admin-order-list.png`. | Future visual polish | `BulkyBookWeb/Areas/Admin/Views/Product/Index.cshtml`, `BulkyBookWeb/Areas/Admin/Views/Order/Index.cshtml`, `BulkyBookWeb/wwwroot/css/site.css` | Deferred; acceptable for staging if horizontal scroll works |
| DEF-008 | G | Automated visual capture timed out before the final Admin Order Details 390px screenshot. | Batch G visual capture produced 47/48 screenshots; missing `admin-390-admin-order-details.png`. | Manual staging sign-off | `BulkyBookWeb/Areas/Admin/Views/Order/Details.cshtml`, `BulkyBookWeb/wwwroot/css/site.css` | Manual check required before staging sign-off |
