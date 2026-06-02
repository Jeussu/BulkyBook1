# Somee Post-Deploy Smoke Test

Run immediately after uploading/publishing to Somee. Capture screenshots, but do not capture secrets or full Identity token URLs.

## Public App

| ID | Priority | Steps | Expected |
|---|---|---|---|
| `SMOKE-01` | P0 | Open `https://<your-somee-domain>` | Home page loads over HTTPS. |
| `SMOKE-02` | P0 | Open a product details page | Product image, title, price, and add-to-cart controls render. |
| `SMOKE-03` | P0 | Open `/Identity/Account/Login` and `/Identity/Account/ForgotPassword` | Identity pages load without runtime errors. |

## Identity Email

| ID | Priority | Steps | Expected |
|---|---|---|---|
| `SMOKE-04` | P0 | Submit Forgot Password for a confirmed test user | Real SMTP email arrives; no LocalFile wording appears. |
| `SMOKE-05` | P0 | Submit Resend Email Confirmation for an unconfirmed test user | Real SMTP email arrives and confirmation link works. |

## Customer Purchase

| ID | Priority | Steps | Expected |
|---|---|---|---|
| `SMOKE-06` | P0 | Login as Individual, add item to cart, verify badge | Badge equals total quantity. |
| `SMOKE-07` | P0 | Submit individual checkout | With valid Stripe config, payment flow works; without required Stripe config, app blocks cleanly without creating bad order. |
| `SMOKE-08` | P0 | Login as Company, submit delayed-payment checkout | Order is approved for delayed payment and `PaymentDueDate` is set. |
| `SMOKE-09` | P0 | Open `My Orders` as customer | Only current user's orders are visible. |

## Staff/Admin

| ID | Priority | Steps | Expected |
|---|---|---|---|
| `SMOKE-10` | P1 | Login as Employee/Admin and open Admin Orders | Order list loads. |
| `SMOKE-11` | P1 | Login as Admin and open Product/Company/Category pages | Admin pages load and validation works. |

## Data Verification

Run sanitized DB checks for:

- Latest Identity user `EmailConfirmed`.
- Latest order header `OrderStatus`, `PaymentStatus`, `OrderTotal`, `PaymentDueDate`.
- Latest order details and product stock.
- Current user's cart rows after checkout.

Do not paste connection strings, passwords, token URLs, or full SMTP provider logs into evidence.
