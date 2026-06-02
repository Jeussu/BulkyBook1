# BulkyBook UI/UX Test Cases

Run these after functional P0 fixes are in place. Capture screenshots at desktop width, tablet width, and a narrow mobile width. Do not include tokens, passwords, SMTP secrets, Stripe keys, or connection strings in screenshots.

## UX Review Summary

The app is a full MVC/Razor e-commerce application. The target UI should be practical and workflow-focused: browse catalog, manage cart, check out, view orders, and perform admin operations without visual blockers. This pass did not make broad redesigns.

## Cases

| ID | Priority | Role | Preconditions | Steps | Expected UI | Expected DB | Evidence / Screenshot | Pass / Fail | Related file / method | Fix needed |
|---|---|---|---|---|---|---|---|---|---|---|
| `UX-01` | P1 | Anonymous | App running | Open Home and product Details on desktop/mobile. | Product cards/images load, text does not overlap, primary actions are visible. | No DB change. | Screenshots. | TBD | `Customer/Home/Index`, `Details` | Fix layout if product browsing is blocked. |
| `UX-02` | P1 | Individual/Company | User logged in; product stock available. | Add to cart from Details; navigate Home, Cart, Summary. | Cart badge visible and correct; quantity controls are easy to use; validation messages visible. | Cart rows match UI. | Screenshots plus cart query. | TBD | `Cart/Index.cshtml`, `Cart/Summary.cshtml` | Fix any blocked checkout path. |
| `UX-03` | P1 | Individual/Company | Cart has items. | Submit Summary with empty/placeholder shipping fields. | Errors appear near fields or summary; no confusing fake local values. | No order created. | Screenshots plus latest order query. | TBD | `Cart/Summary.cshtml`, `CartController` | Fix if fake data can be submitted. |
| `UX-04` | P1 | Individual/Company | User has at least one order. | Open `My Orders`, filter statuses, open detail. | Table loads, filters readable, detail totals/status readable. | No DB change. | Screenshots. | TBD | `Admin/Order/Index`, `Details` | Fix customer order navigation if blocked. |
| `UX-05` | P1 | Admin/Employee | Staff account exists. | Open Admin Orders list/detail; try action buttons by status. | Staff actions are clear; disabled/unavailable actions do not confuse status. | DB changes only for submitted status actions. | Screenshots. | TBD | `Admin/Order/Details.cshtml` | Fix if staff cannot complete order workflow. |
| `UX-06` | P1 | Admin | Admin account exists. | Open Category, CoverType, Product, Company CRUD screens. | Forms are usable; validation messages readable; product image preview/upload does not break layout. | Rows change only after valid submit. | Screenshots. | TBD | Admin views/controllers | Fix blocking CRUD layout. |
| `UX-07` | P1 | Anonymous/new user | SMTP configured. | Register, then view Register Confirmation. | No direct confirmation token link outside Development LocalFile; user is told to check email. | User remains unconfirmed until inbox link is clicked. | Screenshot. | TBD | `RegisterConfirmation.cshtml` | Direct token link outside LocalFile Development is a P1 security/UI issue. |
| `UX-08` | P1 | Any Identity user | SMTP configured. | Forgot Password and Resend Confirmation. | No LocalFile notice in SMTP mode; failure messages do not expose secrets. | Identity rows change only after valid link use. | Screenshots. | TBD | Identity pages | Fix misleading or secret-revealing messages. |
| `UX-09` | P2 | Any | Browser at mobile width. | Inspect navbar, footer, validation forms, order tables. | No horizontal scroll that blocks use; controls remain clickable. | No DB change. | Screenshots. | TBD | Layout and views | Fix visual blockers before deploy. |
| `UX-10` | P2 | Any | Browser dev tools/network available. | Trigger 403, validation failure, payment config failure. | Error/validation states are understandable and actionable. | No unintended DB changes. | Screenshots. | TBD | Controllers/views | Improve copy if users cannot recover. |

## Low-Risk Fixes Already Applied

- Identity email pages now distinguish `LocalFile`, `Smtp`, not configured, and failed delivery states.
- `RegisterConfirmation` no longer presents a direct confirmation link when a real email provider is active.
- Development diagnostics page displays sanitized email configuration without secrets.

## Items To Avoid In This Pass

- Do not redesign layouts broadly.
- Do not alter DB schema for UI polish.
- Do not add new JS frameworks or a new design system.
