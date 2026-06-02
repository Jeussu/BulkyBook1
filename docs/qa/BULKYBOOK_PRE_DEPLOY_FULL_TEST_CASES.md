# BulkyBook Pre-Deploy Full Test Cases

Use this pack for final manual QA before Somee/staging/production deployment. Do not reset, reseed, migrate, or expose secrets during testing.

## Project Scan Snapshot

| Area | Current state |
|---|---|
| Application type | Full ASP.NET Core MVC/Razor web app, not API-only |
| Target framework | `.NET 8` projects: `BulkyBook.Models`, `BulkyBook.DataAccess`, `BulkyBook.Utility`, `BulkyBookWeb` |
| Web layer | MVC controllers, Razor Views, Razor Pages for Identity, ViewComponents, static assets under `wwwroot` |
| Data layer | EF Core SQL Server through `ApplicationDbContext`, repository/unit-of-work pattern, Identity tables |
| Auth | ASP.NET Core Identity with roles: `Admin`, `Employee`, `Individual`, `Company` |
| Email | `IEmailDeliveryService` with `LocalFile` and `Smtp` providers |
| Payment | Stripe Checkout for individual users; delayed payment for company users; Development-only local fallback |
| Main business modules | Catalog, cart, checkout, order management, company management, category/cover type/product admin, Identity email |

## Structure

| Path | Purpose |
|---|---|
| `BulkyBookWeb/Program.cs` | DI, middleware, Identity, Stripe, session, routing, startup seed hooks |
| `BulkyBookWeb/Areas/Customer` | Storefront, product details, cart, checkout |
| `BulkyBookWeb/Areas/Admin` | Admin CRUD and order management |
| `BulkyBookWeb/Areas/Identity` | Login, register, forgot password, email confirmation, profile |
| `BulkyBook.DataAccess` | EF Core context, migrations, repositories, seeders |
| `BulkyBook.Models` | Domain/view models |
| `BulkyBook.Utility` | Constants, Stripe settings, email delivery |
| `docs/qa` | QA plans, bug register, test cases |
| `docs/deployment` | Somee/staging deployment guidance |

## Strengths and Weaknesses

| Strength | Notes |
|---|---|
| Clear MVC area separation | Customer/Admin/Identity workflows are easy to test independently. |
| Repository/unit-of-work layer | Keeps most DB access behind a consistent abstraction. |
| Role-aware order access | Staff can manage orders; customers can list only their own orders after the current fix. |
| Development email outbox | `LocalFile` makes local Identity link testing possible without sending real email. |
| SMTP support | Real inbox testing is available through `Email:Provider=Smtp`. |
| Product upload guards | Image extension, content type, size, and path handling are constrained. |

| Weakness / risk | Notes |
|---|---|
| Stripe webhook/idempotency incomplete | Production payment confirmation still needs a durable webhook/idempotency design. |
| Money uses `double` | Monetary fields should become `decimal` in a schema-changing future pass. |
| Startup seed hooks can mutate DB | Somee must keep demo seeding disabled and migrations controlled. |
| Identity scaffold nullable warnings remain | Build passes, but warning cleanup should be scheduled. |
| Full browser QA still required | Stripe, SMTP, and Somee hosting behavior must be verified in the target environment. |

## Test Case Format

Each case should capture:

```text
ID:
Priority:
Role:
Preconditions:
Steps:
Expected UI:
Expected DB:
Evidence / Screenshot:
Pass / Fail:
Related file / method:
Fix needed:
```

## A. Build and Configuration

### ENV-01 Release Build Baseline

| Field | Value |
|---|---|
| ID | `ENV-01` |
| Priority | P0 |
| Role | QA/Developer |
| Preconditions | No DB reset/migration command pending. |
| Steps | Run `git branch --show-current`, `git status --short`, `dotnet sln BulkyBook.sln list`, `dotnet build BulkyBook.sln --configuration Release --nologo`. |
| Expected UI | N/A |
| Expected DB | No DB changes. |
| Evidence / Screenshot | Attach command output without secrets. |
| Pass / Fail | Build succeeds with 0 errors. |
| Related file / method | `BulkyBook.sln` |
| Fix needed | Any build error blocks deployment. |

### ENV-02 Secret Hygiene

| Field | Value |
|---|---|
| ID | `ENV-02` |
| Priority | P0 |
| Role | QA/Developer |
| Preconditions | Local SMTP/Stripe/Facebook values configured through user-secrets or environment variables. |
| Steps | Search committed files for real connection strings, SMTP passwords, Gmail app passwords, Stripe keys, and Facebook secrets. |
| Expected UI | N/A |
| Expected DB | No DB changes. |
| Evidence / Screenshot | Attach sanitized scan result only. |
| Pass / Fail | No real secret value appears in source/docs/publish output. |
| Related file / method | `appsettings*.json`, `docs/**` |
| Fix needed | Remove secret from repo and rotate it. |

## B. Identity and Email

### ID-01 Forgot Password, Confirmed User, SMTP

| Field | Value |
|---|---|
| ID | `ID-01` |
| Priority | P0 |
| Role | Confirmed user |
| Preconditions | `Email:Provider=Smtp`; target user exists; `EmailConfirmed=1`; app restarted. |
| Steps | Open `/Admin/Diagnostics/Email` as Admin in Development; verify provider; submit `/Identity/Account/ForgotPassword`; click reset link from real inbox; set temporary password; log in. |
| Expected UI | No LocalFile notice; generic success message; reset page accepts valid token once. |
| Expected DB | `PasswordHash` and `SecurityStamp` change. |
| Evidence / Screenshot | Sanitized diagnostics page, inbox screenshot without token exposure, DB before/after hashes redacted if needed. |
| Pass / Fail | Real email arrives and reset works. |
| Related file / method | `ForgotPassword.cshtml.cs`, `EmailSender.cs` |
| Fix needed | Provider/config/auth issue if email does not arrive. |

### ID-02 Resend Email Confirmation, Unconfirmed User, SMTP

| Field | Value |
|---|---|
| ID | `ID-02` |
| Priority | P0 |
| Role | Unconfirmed user |
| Preconditions | `Email:Provider=Smtp`; target user exists; `EmailConfirmed=0`; app restarted. |
| Steps | Submit `/Identity/Account/ResendEmailConfirmation`; click confirmation link from real inbox. |
| Expected UI | Generic success message; no account enumeration. |
| Expected DB | `AspNetUsers.EmailConfirmed` becomes `1`. |
| Evidence / Screenshot | Sanitized inbox and DB evidence. |
| Pass / Fail | Real email arrives and confirmation works. |
| Related file / method | `ResendEmailConfirmation.cshtml.cs`, `EmailSender.cs` |
| Fix needed | Provider/config/auth issue if email does not arrive. |

### ID-03 Register Confirmation Link Exposure

| Field | Value |
|---|---|
| ID | `ID-03` |
| Priority | P1 |
| Role | Anonymous/new user |
| Preconditions | Runtime uses `Email:Provider=Smtp`. |
| Steps | Register a new user; open `RegisterConfirmation` page. |
| Expected UI | Page says to check email; no direct `ConfirmEmail` token link is shown. |
| Expected DB | User remains unconfirmed until inbox link is clicked. |
| Evidence / Screenshot | Register confirmation page without token link. |
| Pass / Fail | Direct confirm link is hidden outside Development LocalFile mode. |
| Related file / method | `RegisterConfirmation.cshtml.cs` |
| Fix needed | Direct token link outside LocalFile Development is a security bug. |

### ID-04 Identity Email Failure Messaging

| Field | Value |
|---|---|
| ID | `ID-04` |
| Priority | P1 |
| Role | Any Identity user |
| Preconditions | Temporarily invalid SMTP config in a non-production test environment. |
| Steps | Trigger Forgot Password, Resend Confirmation, Register, external login confirmation, and Manage Email verification/change. |
| Expected UI | UI does not claim email was sent when delivery failed; Development shows sanitized diagnostics only. |
| Expected DB | Identity data changes only as expected for each flow. |
| Evidence / Screenshot | Screenshot messages without secrets. |
| Pass / Fail | No false success message for failed email delivery. |
| Related file / method | `IEmailDeliveryService`, `Register.cshtml.cs`, `Manage/Email.cshtml.cs` |
| Fix needed | Any silent email failure must route through `IEmailDeliveryService`. |

## C. Catalog and Admin

### ADMIN-01 Admin CRUD

| Field | Value |
|---|---|
| ID | `ADMIN-01` |
| Priority | P1 |
| Role | Admin |
| Preconditions | Admin account exists. |
| Steps | Create/edit/delete Category and CoverType; create/edit Product with valid image; edit Company. |
| Expected UI | Admin pages load, validation messages are clear, DataTables refresh. |
| Expected DB | Created/edited rows match submitted values; deleted rows are removed only when allowed. |
| Evidence / Screenshot | Admin list/detail screenshots and DB row IDs. |
| Pass / Fail | CRUD works without unauthorized access. |
| Related file / method | `ProductController`, `CategoryController`, `CompanyController` |
| Fix needed | Any P0/P1 CRUD runtime error blocks deploy. |

### ADMIN-02 Product Upload Guard

| Field | Value |
|---|---|
| ID | `ADMIN-02` |
| Priority | P1 |
| Role | Admin |
| Preconditions | Admin account; test image files. |
| Steps | Upload valid `.jpg/.png/.webp/.gif`; try invalid extension/content type; try file over 2 MB. |
| Expected UI | Valid image saves; invalid image is rejected. |
| Expected DB | Product image URL changes only after valid upload. |
| Evidence / Screenshot | Validation screenshots. |
| Pass / Fail | No executable or oversized file is accepted. |
| Related file / method | `ProductController.Upsert` |
| Fix needed | File upload bypass is P1/P0 depending impact. |

## D. Cart

### CART-01 Badge Total Quantity

| Field | Value |
|---|---|
| ID | `CART-01` |
| Priority | P0 |
| Role | Individual/Company |
| Preconditions | User cart can be modified. |
| Steps | Add product A quantity 2 and product B quantity 3; refresh Home, Details, and Cart. |
| Expected UI | Badge displays `5`, not `2` and not stale `1`. |
| Expected DB | `SUM(ShoppingCarts.Count)=5`. |
| Evidence / Screenshot | Header badge plus DB query. |
| Pass / Fail | Badge equals total persisted quantity. |
| Related file / method | `ShoppingCartViewComponent.Invoke`, `HomeController.SyncCartSession` |
| Fix needed | Badge/session sync defect if mismatch. |

### CART-02 Update, Remove, Empty Cart

| Field | Value |
|---|---|
| ID | `CART-02` |
| Priority | P0 |
| Role | Individual/Company |
| Preconditions | Cart has at least two lines. |
| Steps | Increment, decrement, remove line, then empty cart through UI actions. |
| Expected UI | Quantities, subtotal, and badge update after each action; empty cart shows badge `0` or no count. |
| Expected DB | Cart rows match UI; empty cart has no rows for user. |
| Evidence / Screenshot | Cart page and DB query after each action. |
| Pass / Fail | No stale session value remains. |
| Related file / method | `CartController.Plus/Minus/Remove`, `ShoppingCartViewComponent` |
| Fix needed | Any stale badge or orphan row is P0. |

## E. Checkout

### CHECKOUT-01 Individual Payment, Stripe/Fallback

| Field | Value |
|---|---|
| ID | `CHECKOUT-01` |
| Priority | P0 |
| Role | Individual |
| Preconditions | Product stock available; either real Stripe test keys or Development local fallback enabled. |
| Steps | Add product, open Summary, enter realistic shipping fields, submit checkout, complete payment/fallback. |
| Expected UI | Checkout completes; no fake local shipping values submitted. |
| Expected DB | Order header/detail created, stock decreases, cart clears, payment fields set according to Stripe/fallback. |
| Evidence / Screenshot | Summary, confirmation, DB queries. |
| Pass / Fail | Order can be placed reliably. |
| Related file / method | `CartController.SummaryPOST`, `CartController.OrderConfirmation` |
| Fix needed | Payment/order failure is P0. |

### CHECKOUT-02 Stripe Missing, Fallback Disabled

| Field | Value |
|---|---|
| ID | `CHECKOUT-02` |
| Priority | P0 |
| Role | Individual |
| Preconditions | No `Stripe:SecretKey`; `Stripe:EnableLocalCheckoutFallback=false`. |
| Steps | Submit checkout. |
| Expected UI | Clear payment configuration error; user remains able to retry. |
| Expected DB | No order; no stock decrement; cart remains. |
| Evidence / Screenshot | Error message and DB query. |
| Pass / Fail | App does not create unpaid/invalid order. |
| Related file / method | `CartController.SummaryPOST` |
| Fix needed | Silent order creation is P0. |

### CHECKOUT-03 Company Delayed Payment

| Field | Value |
|---|---|
| ID | `CHECKOUT-03` |
| Priority | P0 |
| Role | Company |
| Preconditions | Company user has valid `CompanyId`; stock available. |
| Steps | Submit checkout without Stripe. |
| Expected UI | Confirmation page loads. |
| Expected DB | `PaymentStatus=ApprovedForDelayedPayment`; `OrderStatus=Approved`; `PaymentDueDate` is set; cart clears; stock decreases. |
| Evidence / Screenshot | Confirmation and DB order header. |
| Pass / Fail | Delayed payment order succeeds. |
| Related file / method | `CartController.SummaryPOST` |
| Fix needed | Missing due date/order failure is P0. |

### CHECKOUT-04 Required Shipping Fields

| Field | Value |
|---|---|
| ID | `CHECKOUT-04` |
| Priority | P0 |
| Role | Individual/Company |
| Preconditions | Cart has items. |
| Steps | Clear required fields and submit. |
| Expected UI | Validation blocks checkout. |
| Expected DB | No order and no stock decrement. |
| Evidence / Screenshot | Validation summary and DB query. |
| Pass / Fail | Required shipping data enforced. |
| Related file / method | `CartController.ValidateCheckoutProfile` |
| Fix needed | Missing validation is P0. |

### CHECKOUT-05 Order Total Tampering

| Field | Value |
|---|---|
| ID | `CHECKOUT-05` |
| Priority | P0 |
| Role | Individual/Company |
| Preconditions | Cart has items; browser dev tools or proxy available. |
| Steps | Modify posted `OrderHeader.OrderTotal` and submit. |
| Expected UI | Checkout uses server-calculated total. |
| Expected DB | Stored total equals sum of order detail prices/counts, not posted value. |
| Evidence / Screenshot | Captured POST and DB total calculation. |
| Pass / Fail | Posted total cannot affect order total. |
| Related file / method | `CartController.RemoveServerControlledCheckoutModelState`, `SummaryPOST` |
| Fix needed | Client-controlled total is P0. |

## F. Orders

### ORDER-01 Customer My Orders

| Field | Value |
|---|---|
| ID | `ORDER-01` |
| Priority | P0 |
| Role | Individual/Company |
| Preconditions | User has at least one order. |
| Steps | Click `My Orders`; filter statuses; open own order details. |
| Expected UI | Customer can list only own orders. |
| Expected DB | API query returns only current user's orders. |
| Evidence / Screenshot | My Orders list and network response. |
| Pass / Fail | No admin-only restriction blocks customer list. |
| Related file / method | `OrderController.Index`, `OrderController.GetAll` |
| Fix needed | Access failure or data leak is P0. |

### ORDER-02 Cross-User Order Access

| Field | Value |
|---|---|
| ID | `ORDER-02` |
| Priority | P0 |
| Role | Individual/Company |
| Preconditions | Two customer accounts with orders. |
| Steps | Change order detail `id` to another user's order. |
| Expected UI | Access denied/not found; no details leak. |
| Expected DB | No DB mutation. |
| Evidence / Screenshot | Denied page/result. |
| Pass / Fail | Customer cannot view another customer's order. |
| Related file / method | `OrderController.Details` |
| Fix needed | Data leak is P0. |

## G. Authorization and Security

### AUTH-01 Area Authorization Matrix

| Field | Value |
|---|---|
| ID | `AUTH-01` |
| Priority | P0 |
| Role | Anonymous, Individual, Company, Employee, Admin |
| Preconditions | Test accounts for all roles. |
| Steps | Try Customer pages, cart, checkout, Admin CRUD, and Admin orders for each role. |
| Expected UI | Anonymous can browse catalog; authenticated customers can buy; Employee can manage orders; Admin can manage all admin areas. |
| Expected DB | Unauthorized attempts do not mutate DB. |
| Evidence / Screenshot | Access denied/login redirects and allowed pages. |
| Pass / Fail | Role matrix matches business rules. |
| Related file / method | Controller `[Authorize]` attributes |
| Fix needed | Privilege escalation is P0. |

## H. UI/UX

### UX-01 Core Responsive Smoke

| Field | Value |
|---|---|
| ID | `UX-01` |
| Priority | P1 |
| Role | Any |
| Preconditions | Browser at desktop and mobile widths. |
| Steps | Browse Home, Details, Cart, Summary, My Orders, Admin Orders, Identity forms. |
| Expected UI | No overlapping text, broken navbar, hidden submit buttons, or unreadable validation messages. |
| Expected DB | No unexpected DB changes unless a form is submitted. |
| Evidence / Screenshot | Desktop and mobile screenshots. |
| Pass / Fail | User can complete core flows on common viewport sizes. |
| Related file / method | Razor views under `BulkyBookWeb/Areas/**/Views` and Identity pages |
| Fix needed | Blocking visual defect is P1/P0 if it blocks checkout/auth. |

## I. Data Integrity

### DATA-01 Stock and Historical Orders

| Field | Value |
|---|---|
| ID | `DATA-01` |
| Priority | P1 |
| Role | Admin/Customer |
| Preconditions | Product with stock and historical order. |
| Steps | Place order; try deleting product with history; inspect stock/order details. |
| Expected UI | Historical product deletion is blocked or safe; order detail remains readable. |
| Expected DB | Stock decreases once per successful order; order details remain intact. |
| Evidence / Screenshot | Product/order DB query. |
| Pass / Fail | No broken historical order rows. |
| Related file / method | `ProductController.Delete`, `CartController.SummaryPOST` |
| Fix needed | Historical data break is P1/P0. |

## J. Somee Deployment

### DEPLOY-01 Somee Environment Variables

| Field | Value |
|---|---|
| ID | `DEPLOY-01` |
| Priority | P0 |
| Role | Developer/Operator |
| Preconditions | Somee app and SQL database exist. |
| Steps | Configure required environment variables; verify no secrets in source; publish; upload. |
| Expected UI | App starts on HTTPS domain. |
| Expected DB | App connects to intended DB only. |
| Evidence / Screenshot | Sanitized Somee config screenshots. |
| Pass / Fail | App launches without secret exposure or DB reset. |
| Related file / method | `docs/deployment/SOMEE_ENVIRONMENT_VARIABLES.md` |
| Fix needed | Missing env var blocks deploy. |

## K. Final Signoff

### SIGNOFF-01 Production Readiness

| Field | Value |
|---|---|
| ID | `SIGNOFF-01` |
| Priority | P0 |
| Role | Project owner |
| Preconditions | All P0 tests passed; P1 risks accepted or fixed. |
| Steps | Review build, QA evidence, bug register, deployment checklist, and smoke test result. |
| Expected UI | All critical user journeys pass. |
| Expected DB | No unintended migrations/resets/seeding. |
| Evidence / Screenshot | Completed signoff checklist. |
| Pass / Fail | Deployment approved only when P0 is clear. |
| Related file / method | `BULKYBOOK_PRE_DEPLOY_SIGNOFF_CHECKLIST.md` |
| Fix needed | Any open P0 blocks deploy. |
