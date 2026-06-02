# Somee Staging Smoke Test Execution

Run these tests immediately after uploading and restarting the Somee staging/demo site. Do not capture secrets, passwords, full token URLs, Stripe keys, SMTP passwords, Facebook secrets, or DB connection strings.

| ID | Priority | URL/path | Role | Steps | Expected result | Evidence | Pass/Fail | Notes |
|---|---|---|---|---|---|---|---|---|
| `SMOKE-01` | P0 | `/` | Anonymous | Open the HTTPS Somee domain. | Home page loads over HTTPS without runtime error. | Screenshot of home page and URL bar. |  |  |
| `SMOKE-02` | P0 | `/Customer/Home/Details/{id}` | Anonymous | Open one product details page from Home. | Product title, image, price, stock/add-to-cart UI render. | Product detail screenshot. |  |  |
| `SMOKE-03` | P0 | `/Identity/Account/Login`, `/Identity/Account/Register`, `/Identity/Account/ForgotPassword` | Anonymous | Open Login, Register, and Forgot Password pages. | Identity pages render without errors. | Screenshots of each page. |  |  |
| `SMOKE-04` | P0 | `/Identity/Account/ForgotPassword` | Confirmed user | Submit Forgot Password for a confirmed test user. | Real SMTP email arrives; no `LocalFile` wording appears; reset link works. | Inbox screenshot without full token URL; DB evidence that `SecurityStamp` changed after reset. |  |  |
| `SMOKE-05` | P0 | `/Identity/Account/ResendEmailConfirmation` | Unconfirmed user | Submit Resend Email Confirmation for an unconfirmed user. | Real SMTP email arrives; confirmation link sets `EmailConfirmed=1`. | Inbox screenshot without full token URL; DB `EmailConfirmed` evidence. |  |  |
| `SMOKE-06` | P0 | Product Details, Cart | Customer | Log in, add product A quantity 2 and product B quantity 3. | Cart badge equals total quantity `5`; cart rows match UI. | Header/cart screenshots and cart DB query. |  |  |
| `SMOKE-07` | P0 | `/Customer/Cart/Summary` | Individual | Submit individual checkout using configured Stripe test keys or expected staging payment path. | Order completes with correct total, stock decrease, cart clear; if Stripe missing, checkout blocks cleanly without creating a bad order. | Order confirmation/error screenshot plus latest order/cart/stock DB query. |  |  |
| `SMOKE-08` | P0 | `/Customer/Cart/Summary` | Company | Submit delayed-payment checkout as a company user. | Order is approved for delayed payment; `PaymentDueDate` is set; cart clears; stock decreases. | Confirmation screenshot and latest order DB query. |  |  |
| `SMOKE-09` | P0 | `/Admin/Order/Index` | Customer | Open `My Orders`; try changing detail `id` to another user's order. | Own orders are visible; another user's order is denied/not found. | My Orders screenshot and denied/not-found evidence. |  |  |
| `SMOKE-10` | P1 | `/Admin/Order/Index` | Employee/Admin | Open order list and one order detail page. | Staff order list and details load; actions match order status. | Staff order list/detail screenshots. |  |  |
| `SMOKE-11` | P1 | `/Admin/Product`, `/Admin/Company`, `/Admin/Category` | Admin | Open Product, Company, and Category admin pages. | Admin pages load; DataTables/forms render; validation works for invalid input. | Admin page screenshots. |  |  |
| `SMOKE-12` | P1 | `wwwroot` assets via UI | Any | Browse Home, Details, Cart, Admin pages; inspect browser network. | CSS, JS, images, product covers, and DataTables assets load without 404s. | Screenshot/network evidence with no secrets. |  |  |
| `SMOKE-13` | P1 | Somee logs | Operator | Review hosting logs after smoke tests. | No recurring startup/runtime exception; no unintended migration/reset/seed activity. | Sanitized log excerpt. |  |  |
| `SMOKE-14` | P0 | Published site/config evidence | Operator | Check rendered pages, public files, and uploaded package evidence. | No secret values exposed in pages, source downloads, logs, screenshots, or uploaded files. | Secret exposure checklist with key names only. |  |  |

## DB Evidence Queries

Use sanitized DB screenshots or query output. Do not include DB connection strings or user passwords.

Identity:

```sql
DECLARE @Email nvarchar(256) = N'<target-email>';

SELECT Id, Email, EmailConfirmed, SecurityStamp
FROM AspNetUsers
WHERE NormalizedEmail = UPPER(@Email);
```

Cart:

```sql
DECLARE @Email nvarchar(256) = N'<target-email>';

SELECT COUNT(*) AS DistinctCartLines, COALESCE(SUM(sc.Count), 0) AS TotalCartQuantity
FROM ShoppingCarts sc
JOIN AspNetUsers u ON u.Id = sc.ApplicationUserId
WHERE u.NormalizedEmail = UPPER(@Email);
```

Latest order:

```sql
DECLARE @Email nvarchar(256) = N'<target-email>';

SELECT TOP (3)
    oh.Id,
    oh.OrderStatus,
    oh.PaymentStatus,
    oh.OrderTotal,
    oh.PaymentDueDate,
    oh.OrderDate
FROM OrderHeaders oh
JOIN AspNetUsers u ON u.Id = oh.ApplicationUserId
WHERE u.NormalizedEmail = UPPER(@Email)
ORDER BY oh.Id DESC;
```

Stock:

```sql
DECLARE @OrderId int = <order-id>;

SELECT od.OrderId, od.ProductId, p.Title, od.Count, od.Price, p.StockQuantity
FROM OrderDetail od
JOIN Products p ON p.Id = od.ProductId
WHERE od.OrderId = @OrderId;
```

## Exit Criteria

- All P0 smoke tests pass or have a documented accepted staging limitation.
- No real secret appears in screenshots, logs, public pages, or uploaded files.
- Any failed P0 smoke test is treated as a staging blocker until fixed or explicitly accepted for demo-only use.
