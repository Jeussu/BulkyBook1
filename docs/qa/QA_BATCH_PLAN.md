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

## Deferred Issue Register

Use this section after each batch to record deeper findings discovered outside the current allowed scope.

| Issue ID | Found in batch | Summary | Evidence | Assigned batch | Suggested files | Status |
|---|---|---|---|---|---|---|
| DEF-001 | B | No deeper issue recorded yet. Update this table if Batch B smoke finds a business/security/data defect outside its allowed scope. | N/A | C/D/E/F | N/A | Placeholder |
