# Runtime Manual QA Package

This package is designed for manual verification of checkout, cart, Identity email, role access, and database side effects. It does not require database reset, seeding, migrations, or schema changes.

## Safety Rules

- Do not run `dotnet ef database update`, `dotnet ef migrations add`, or any DB reset command during this QA pass.
- Do not paste real passwords, SMTP credentials, Stripe keys, or connection strings into test evidence.
- Use environment variables, user secrets, or hosting configuration for real secrets.
- Local development emails are written under `BulkyBookWeb/App_Data/dev-mails/` when `Email:Provider` is `LocalFile`.
- `Email:Provider=LocalFile` never sends to Gmail, Outlook, Mailtrap, or any SMTP inbox. It only writes local `.html` files for development testing.
- Real inbox delivery requires `Email:Provider=Smtp` and valid SMTP settings.
- `BulkyBookWeb/App_Data/dev-mails/` is ignored by git and should not be committed.

## Baseline Commands

Run these before testing and attach the output to the QA evidence.

```powershell
git branch --show-current
git status --short
dotnet --info
dotnet sln BulkyBook.sln list
dotnet build BulkyBook.sln --nologo
```

If the Debug build is blocked by a running `BulkyBookWeb.exe` or Visual Studio process, keep the process running and verify compilation with:

```powershell
dotnet build BulkyBook.sln --configuration Release --nologo
```

## Required Test Users

Use existing users from the target environment. Do not create or publish passwords in evidence.

- `Admin`: user in role `Admin`
- `Employee`: user in role `Employee`
- `Individual`: user in role `Individual`, `CompanyId` is null or 0
- `Company`: user in role `Company`, `CompanyId` points to an active company
- `Unconfirmed`: user with `EmailConfirmed = 0`

## Configuration Matrix

### Local Development Email

Expected configuration:

```json
"Email": {
  "Provider": "LocalFile",
  "LocalFile": {
    "Directory": "App_Data/dev-mails"
  }
}
```

Expected result: confirmation and reset emails are saved as `.html` files in `BulkyBookWeb/App_Data/dev-mails/`.

LocalFile test steps:

1. Confirm `ASPNETCORE_ENVIRONMENT` is `Development`.
2. Confirm `Email:Provider` is `LocalFile`.
3. Delete only old files in `BulkyBookWeb/App_Data/dev-mails/` if you want a clean test evidence folder.
4. Submit `/Identity/Account/ResendEmailConfirmation` or `/Identity/Account/ForgotPassword`.
5. Do not check Gmail for this mode.
6. Open the newest `.html` file from `BulkyBookWeb/App_Data/dev-mails/` in a browser.
7. Click the confirmation/reset link inside the local file.

Expected result: the UI says LocalFile mode is active, no Gmail/SMTP delivery occurs, and the local file contains the usable Identity link.

### SMTP Email

For a focused real-inbox setup guide, use `docs/qa/SMTP_EMAIL_TESTING.md`.
For provider tradeoffs, use `docs/qa/EMAIL_PROVIDER_OPTIONS.md`.

Expected configuration keys:

```text
Email:Provider = Smtp
Email:Smtp:Host = <smtp-host>
Email:Smtp:Port = <smtp-port>
Email:Smtp:SecureSocketOptions = StartTls|SslOnConnect|None|Auto
Email:Smtp:UseStartTls = true|false
Email:Smtp:Username = <optional-user>
Email:Smtp:Password = <optional-secret>
Email:Smtp:From = <from-address>
```

For unauthenticated local SMTP tools such as Papercut/MailHog, set `Username` and `Password` both empty and set `UseStartTls` according to the tool.

Real SMTP test steps:

1. Set `Email:Provider` to `Smtp`.
2. Set `Email:Smtp:Host`, `Email:Smtp:Port`, `Email:Smtp:UseStartTls`, and `Email:Smtp:From`.
3. Set `Email:Smtp:Username` and `Email:Smtp:Password` only when the SMTP provider requires authentication.
4. For Gmail SMTP, use Google's current SMTP settings and an app password or approved authentication method. Google documents `smtp.gmail.com`, STARTTLS port `587`, and authentication requirements in Gmail Help: https://support.google.com/mail/answer/86377
5. Restart the app so configuration is reloaded.
6. In Development, sign in as Admin and open `/Admin/Diagnostics/Email`; verify `Provider=Smtp` and `IsConfigured=True`.
7. Submit `/Identity/Account/ResendEmailConfirmation` for an unconfirmed account.
8. Submit `/Identity/Account/ForgotPassword` for a confirmed account.
9. Check the real recipient inbox and spam folder.
10. Confirm no new file is required under `BulkyBookWeb/App_Data/dev-mails/` in SMTP mode.

Expected result: the app attempts real SMTP delivery, the UI does not mention LocalFile mode, and the email arrives in the configured recipient inbox if SMTP credentials/provider settings are valid.

### Checkout Payment

- Individual checkout requires real Stripe keys unless local fallback is enabled in Development.
- Company checkout should bypass Stripe and create an approved delayed-payment order.
- Do not enable local fallback outside Development.

## Database Helper Queries

Replace placeholders before running.

### User and Role

```sql
DECLARE @Email nvarchar(256) = N'<user-email>';

SELECT Id, Email, EmailConfirmed, PhoneNumber, LockoutEnd
FROM AspNetUsers
WHERE NormalizedEmail = UPPER(@Email);

SELECT u.Email, r.Name AS RoleName
FROM AspNetUsers u
JOIN AspNetUserRoles ur ON ur.UserId = u.Id
JOIN AspNetRoles r ON r.Id = ur.RoleId
WHERE u.NormalizedEmail = UPPER(@Email);
```

### Cart Badge and Cart Lines

```sql
DECLARE @Email nvarchar(256) = N'<user-email>';

SELECT sc.Id, sc.ProductId, p.Title, sc.Count, p.StockQuantity, p.IsActive
FROM ShoppingCarts sc
JOIN AspNetUsers u ON u.Id = sc.ApplicationUserId
JOIN Products p ON p.Id = sc.ProductId
WHERE u.NormalizedEmail = UPPER(@Email)
ORDER BY sc.Id;

SELECT
    COUNT(*) AS DistinctCartLines,
    COALESCE(SUM(sc.Count), 0) AS TotalCartQuantity
FROM ShoppingCarts sc
JOIN AspNetUsers u ON u.Id = sc.ApplicationUserId
WHERE u.NormalizedEmail = UPPER(@Email);
```

### Latest Order

```sql
DECLARE @Email nvarchar(256) = N'<user-email>';

SELECT TOP (5)
    oh.Id,
    oh.ApplicationUserId,
    u.Email,
    oh.OrderDate,
    oh.OrderTotal,
    oh.OrderStatus,
    oh.PaymentStatus,
    oh.SessionId,
    oh.PaymentIntentId,
    oh.PaymentDate,
    oh.PaymentDueDate,
    oh.Name,
    oh.PhoneNumber,
    oh.StreetAddress,
    oh.City,
    oh.State,
    oh.PostalCode
FROM OrderHeaders oh
JOIN AspNetUsers u ON u.Id = oh.ApplicationUserId
WHERE u.NormalizedEmail = UPPER(@Email)
ORDER BY oh.Id DESC;
```

### Latest Order Details and Stock

```sql
DECLARE @OrderId int = <order-id>;

SELECT od.Id, od.OrderId, od.ProductId, p.Title, od.Count, od.Price, p.StockQuantity
FROM OrderDetail od
JOIN Products p ON p.Id = od.ProductId
WHERE od.OrderId = @OrderId
ORDER BY od.Id;
```

### Password Reset Verification

Capture before and after values without exposing password text.

```sql
DECLARE @Email nvarchar(256) = N'<user-email>';

SELECT Id, Email, EmailConfirmed, PasswordHash, SecurityStamp
FROM AspNetUsers
WHERE NormalizedEmail = UPPER(@Email);
```

Expected result after a successful reset: `PasswordHash` and `SecurityStamp` change.

## Manual Test Cases

### ID-01 Resend Confirmation, Unconfirmed User, Local Outbox

Preconditions:

- `Email:Provider` is `LocalFile`.
- Target user exists and `EmailConfirmed = 0`.

Steps:

1. Open `/Identity/Account/ResendEmailConfirmation`.
2. Enter the unconfirmed user's email.
3. Submit.
4. Open the newest `.html` file in `BulkyBookWeb/App_Data/dev-mails/`.
5. Click the `ConfirmEmail` link.
6. Re-run the user query.

Expected:

- Page displays an informational message, not a red validation error.
- Message says LocalFile mode does not send to Gmail/SMTP.
- A local outbox `.html` file is created.
- Confirmation link opens the Identity confirm page.
- `AspNetUsers.EmailConfirmed` becomes `1`.

### ID-02 Resend Confirmation, Unknown or Already Confirmed User

Steps:

1. Submit an unknown email.
2. Submit an already confirmed user's email.

Expected:

- Message remains generic.
- The UI does not reveal whether the account exists.
- No new confirmation email is required for an already confirmed account.

### ID-03 Forgot Password, Confirmed User, Local Outbox

Preconditions:

- Target user exists and `EmailConfirmed = 1`.
- Capture `PasswordHash` and `SecurityStamp` before the test.

Steps:

1. Open `/Identity/Account/ForgotPassword`.
2. Submit the confirmed user's email.
3. Open the newest local outbox `.html` file.
4. Click the `ResetPassword` link.
5. Complete the reset with a temporary QA password.
6. Log in with the new password.
7. Re-run the password reset verification query.

Expected:

- Forgot password confirmation does not falsely say SMTP was sent when local outbox is used.
- Confirmation page says LocalFile mode does not send to Gmail/SMTP.
- A reset email file is created in local outbox.
- Reset page accepts the token once.
- User can log in with the new password.
- `PasswordHash` and `SecurityStamp` change.

### ID-04 Forgot Password, Unconfirmed or Unknown User

Steps:

1. Submit an unconfirmed user's email.
2. Submit an unknown email.

Expected:

- Message remains generic.
- The UI does not reveal whether the account exists or is unconfirmed.
- No reset email should be created for the unconfirmed user.

### CART-01 Add Items and Badge Quantity

Steps:

1. Log in as `Individual`.
2. Clear existing cart manually through UI if needed.
3. Add product A with quantity 2.
4. Add product B with quantity 3.
5. Refresh the page and inspect the cart badge.
6. Run the cart query.

Expected business rule:

- Badge equals `TotalCartQuantity`, not `DistinctCartLines`.
- Cart page displays both lines and correct subtotal.

Regression target:

- The cart badge should be recalculated from persisted cart rows and should display total item quantity. A stale session value or row count is a defect.

### CART-02 Update and Remove Items

Steps:

1. Increase product A by 1.
2. Decrease product B by 1.
3. Remove product A.
4. Refresh home, details, and cart pages.
5. Run the cart query after each action.

Expected:

- `ShoppingCarts.Count` matches UI.
- Badge updates after each action.
- Removed product no longer appears in cart.

### CHECKOUT-01 Individual Checkout, Development Local Payment Fallback

Preconditions:

- Environment is Development.
- `Stripe:SecretKey` is empty.
- `Stripe:EnableLocalCheckoutFallback` is `true`.
- User has `CompanyId` null or 0.
- Product stock is greater than cart quantity.

Steps:

1. Log in as `Individual`.
2. Add at least one active product to cart.
3. Open cart summary.
4. Replace any fallback profile values with realistic shipping values.
5. Submit checkout.
6. Run latest order, order detail, cart, and stock queries.

Expected:

- Order is created without external Stripe redirect.
- `OrderStatus = Approved`.
- `PaymentStatus = Approved`.
- `SessionId` and `PaymentIntentId` use local demo values.
- Order total equals server-calculated line totals.
- Cart is cleared for that user.
- Product stock decreases by ordered quantity.

### CHECKOUT-02 Individual Checkout, Stripe Missing and Fallback Disabled

Preconditions:

- `Stripe:SecretKey` is empty.
- `Stripe:EnableLocalCheckoutFallback` is `false`.
- User has `CompanyId` null or 0.

Steps:

1. Add product to cart.
2. Submit checkout.
3. Run latest order and cart queries.

Expected:

- Checkout shows an actionable payment configuration error.
- No new order is created.
- Cart remains intact.
- Stock is not decremented.

### CHECKOUT-03 Company Checkout, Delayed Payment

Preconditions:

- User is in role `Company`.
- User has a valid `CompanyId`.
- Product stock is greater than cart quantity.

Steps:

1. Log in as `Company`.
2. Add an active product to cart.
3. Submit checkout.
4. Run latest order, order detail, cart, and stock queries.

Expected:

- Checkout does not redirect to Stripe.
- `OrderStatus = Approved`.
- `PaymentStatus = ApprovedForDelayedPayment`.
- Cart is cleared.
- Stock decreases by ordered quantity.
- `PaymentDueDate` is not the default date and is set to the configured delayed-payment window.

### CHECKOUT-04 Required Shipping Fields

Steps:

1. Log in as any purchasing user.
2. Open checkout summary.
3. Clear one required field such as `PhoneNumber` or `PostalCode`.
4. Submit.

Expected:

- Validation prevents order creation.
- Error is visible beside the field or in validation summary.
- No stock decrement occurs.

### CHECKOUT-05 Order Total Tampering

Steps:

1. Intercept the checkout POST with browser dev tools or a proxy.
2. Add or alter a posted `OrderHeader.OrderTotal` value.
3. Submit checkout.
4. Compare stored order total with cart line calculation in SQL.

Expected business rule:

- Stored `OrderTotal` must be calculated only on the server from cart rows and product prices.

Regression target:

- Posted `OrderHeader.OrderTotal` must not change the stored order total.

### CHECKOUT-06 Placeholder Shipping Profile Values

Preconditions:

- Target user profile contains local placeholder values such as `Local Admin`, `Local Street`, `0000000000`, or `00000`.

Steps:

1. Log in as the target user.
2. Add a product to cart.
3. Open checkout summary.
4. Inspect the shipping fields.
5. Try submitting without replacing placeholder/empty values.
6. Replace all shipping fields with real QA values and submit again.
7. Re-open checkout later with a new cart.

Expected:

- Placeholder profile values are not silently submitted as delivery details.
- Required shipping validation blocks empty or placeholder delivery fields.
- Successful checkout stores the corrected shipping values on the order.
- The user's profile is updated with corrected shipping values for later checkout.

### ORDER-01 Staff Order Management

Steps:

1. Log in as `Admin`.
2. Open `/Admin/Order/Index`.
3. Filter by pending, approved, processing, completed, and all.
4. Open details for a recent order.
5. Repeat as `Employee`.

Expected:

- Admin and Employee can list orders.
- Details page shows payment, shipping, and line items.
- Staff-only actions are available only when the status allows them.

### ORDER-02 Customer Order Access

Steps:

1. Log in as `Individual`.
2. Use the layout link labeled `My Orders`.
3. Try to open another user's order details by changing `id`.

Expected business rule:

- Customer can list their own orders.
- Customer cannot access another user's order details.

Regression target:

- `My Orders` should load the order index for authenticated customers, and the backing API should return only the current user's orders for non-staff users.

### ADMIN-01 Master Data Access

Steps:

1. Log in as `Admin`.
2. Create, edit, and delete a test Category.
3. Create and edit a Product with category, cover type, price tiers, stock, active status, and image.
4. Create or edit a Company.
5. Attempt the same routes as `Employee`, `Individual`, and anonymous.

Expected:

- Admin can access master data CRUD.
- Non-admin users cannot access admin CRUD endpoints.
- Product image upload does not break existing products.
- Products linked to historical orders cannot be deleted if restricted by FK rules.

### AUTH-01 Role and Area Authorization

Steps:

1. Anonymous user opens product list and product details.
2. Anonymous user opens cart or checkout.
3. Individual opens Admin CRUD routes.
4. Employee opens Admin CRUD routes and order routes.
5. Admin opens all Admin routes.

Expected:

- Anonymous users can browse products but are redirected to login for cart/checkout.
- Individual users cannot access Admin CRUD.
- Employee users can manage orders but not master data CRUD.
- Admin users can manage master data and orders.

## Exit Criteria

- Full pre-deploy cases are tracked in `docs/qa/BULKYBOOK_PRE_DEPLOY_FULL_TEST_CASES.md`.
- Open defects and accepted risks are tracked in `docs/qa/BULKYBOOK_PRE_DEPLOY_BUG_REGISTER.md`.
- UI/UX checks are tracked in `docs/qa/BULKYBOOK_UI_UX_TEST_CASES.md`.
- Somee checks are tracked in `docs/qa/BULKYBOOK_SOMEE_DEPLOYMENT_TEST_CASES.md`.
- Final approval is tracked in `docs/qa/BULKYBOOK_PRE_DEPLOY_SIGNOFF_CHECKLIST.md`.
- Release build succeeds.
- Identity confirmation and password reset are testable with either local outbox or SMTP.
- No page claims an email was sent when delivery is not configured.
- Checkout behavior is verified for Individual and Company users.
- Cart badge matches total quantity or the failure is logged as a defect.
- DB verification queries confirm order, cart, stock, and Identity side effects.
- Known defects are filed separately before production deployment.
