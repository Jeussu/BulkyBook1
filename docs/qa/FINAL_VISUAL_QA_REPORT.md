# Final Visual QA Report

Date: 2026-06-01

## Scope

Local visual QA for the finalized BulkyBook UI before Somee/IIS staging upload.

The app was started locally with:

- `ASPNETCORE_ENVIRONMENT=Development`
- `Database__AutoMigrate=false`
- `SeedData__EnableDemoData=false`

No application files were changed during this visual QA pass.

## Pages And Viewports

Viewports requested:

- 1440px
- 1280px
- 768px
- 390px

Pages checked:

- `/Customer/Home/Index`
- `/Customer/Home/Index?page=2&pageSize=8`
- `/Customer/Home/Details?productId=1`
- `/Customer/Cart/Index` after customer login and a local-safe cart item
- `/Customer/Cart/Summary` after customer login and a local-safe cart item
- `/Identity/Account/Login`
- `/Identity/Account/Register`
- `/Admin/Product/Index` after admin login
- `/Admin/Product/Upsert`
- `/Admin/Product/Upsert?id=1`
- `/Admin/Order/Index` after admin login
- `/Admin/Order/Details?orderId=1`

## Screenshot Capture

Browser automation captured 47 of 48 requested screenshots before the Chrome DevTools Protocol run timed out near the final mobile admin order detail capture.

Screenshot folder:

```text
C:\Users\Admin\AppData\Local\Temp\BulkyBook-BatchG-VisualQA-6d17e4d01aeb4b9a957fc58411f28ead
```

Captured coverage:

- Anonymous Home, Home page 2, Product Detail, Login, Register at 1440px, 1280px, 768px, and 390px.
- Customer Cart and Checkout Summary at 1440px, 1280px, 768px, and 390px.
- Admin Product List, Product Create, Product Edit, Order List at 1440px, 1280px, 768px, and 390px.
- Admin Order Details at 1440px, 1280px, and 768px.

Missing automated screenshot:

- Admin Order Details at 390px. This needs one manual mobile-width spot check before staging sign-off.

## Observations

| Area | Result |
|---|---|
| Navbar | Usable at mobile and desktop widths in sampled screenshots. |
| Footer | No overlap observed in sampled screenshots. |
| Customer catalog | Mobile filter/search layout is usable. |
| Product detail | No obvious image distortion or broken layout in sampled screenshots. |
| Cart | Mobile cart item, quantity controls, total hierarchy, and CTA remain readable. |
| Checkout summary | Mobile shipping form and order summary remain readable and stacked correctly. |
| Login/Register | Mobile auth shell and forms remain readable; long register form scrolls normally. |
| Admin product form | Mobile product edit form remains readable; long form scrolls normally. |
| Admin order detail | Desktop/tablet order detail remains readable; action area and order summary are visible. |
| Static assets | No stale SVG book covers observed in captured key pages. |

## Issues Found

| ID | Severity | Area | Evidence | Recommendation |
|---|---|---|---|---|
| VQA-001 | Low | Admin DataTables at 390px | Admin Order List mobile screenshot shows table columns clipped with horizontal scrollbar. | Accept for staging; manually confirm horizontal scrolling works on device. A future polish batch can improve responsive column priority or mobile card rendering. |
| VQA-002 | Low | Home mobile filters | Min/Max price controls create a tall blank-input area on the 390px catalog screenshot. | Accept for staging; future polish can tighten mobile filter spacing. |
| VQA-003 | Manual check needed | Admin Order Details at 390px | Automated capture timed out before this final screenshot. | Manually open `/Admin/Order/Details?orderId=1` at 390px and confirm no major horizontal overflow. |

## Visual Fixes Applied

None. The findings are polish/manual-check items and did not justify a late Batch G app change.

## Final Recommendation

The UI is ready for Somee staging upload after one manual 390px check of Admin Order Details and a quick mobile confirmation that admin tables can be horizontally scrolled.
