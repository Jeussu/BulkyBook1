# BulkyBook Project-Wide Test Case Backlog

This backlog is the source of truth for QA planning after the Stitch UI refresh. It favors behavior preservation, regression coverage, and safe incremental fixes. Batch B covers only the no-login P0 smoke foundation; authenticated and destructive flows are assigned to later batches.

## A. Anonymous User

| Test ID | Area | Role | Preconditions | Steps | Expected result | Data required | Automation feasibility | Priority | Batch assignment | Files likely involved |
|---|---|---|---|---|---|---|---|---|---|---|
| A-001 | Catalog | Anonymous | App running with readable products | Open `/Customer/Home/Index` | Page returns 200, layout renders, product grid visible if products exist | Product seed data | Automated | P0 | B | `Areas/Customer/Views/Home/Index.cshtml`, `wwwroot/css/site.css` |
| A-002 | Catalog search | Anonymous | Products exist | Open `/Customer/Home/Index?searchTerm=core` | Page returns 200, filter value is preserved, no server error | Product titles/authors/ISBNs | Automated | P0 | B | `HomeController`, `Index.cshtml` |
| A-003 | Catalog filters | Anonymous | Categories and cover types exist | Submit category, cover, min price, max price filters | Results render and route query values remain valid | Category/CoverType data | Hybrid | P1 | C | `Index.cshtml`, `HomeController` |
| A-004 | Catalog sorting | Anonymous | Products exist | Change `SortBy` and submit | Products render in expected order or documented fallback | Product prices/titles | Hybrid | P1 | C | `Index.cshtml`, `HomeController` |
| A-005 | Page size | Anonymous | Enough products exist | Change `PageSize` to supported values | Page size applies, pagination links preserve query state | Product seed data | Hybrid | P1 | C | `Index.cshtml`, `HomeController` |
| A-006 | Pagination | Anonymous | Multiple pages exist | Open `/Customer/Home/Index?page=2&pageSize=8` | Page returns 200 and active pagination state is readable | Product seed data | Automated | P0 | B | `Index.cshtml`, `site.css` |
| A-007 | Product detail | Anonymous | Product id 1 exists | Open `/Customer/Home/Details?productId=1` | Detail page returns 200, image/title/price/quantity UI renders | Product id | Automated | P0 | B | `Details.cshtml`, `HomeController` |
| A-008 | Login page | Anonymous | Identity configured | Open `/Identity/Account/Login` | Page returns 200, login form renders, no demo-only copy visible | None | Automated | P0 | B | `Areas/Identity/Pages/Account/Login.cshtml` |
| A-009 | Register page | Anonymous | Identity configured | Open `/Identity/Account/Register` | Page returns 200, register form renders, role/company behavior remains available | None | Automated | P0 | B | `Register.cshtml`, `site.css` |
| A-010 | Protected redirect | Anonymous | Not logged in | Open cart/admin protected URLs | Either 302 to login or 200 if an authenticated session was intentionally supplied | None | Automated | P0 | B | `_Layout.cshtml`, Identity configuration |
| A-011 | Responsive anonymous UI | Anonymous | Browser available | Check 1440, 1280, 768, 390 widths for Home/Login/Register | No major horizontal overflow; nav, forms, and product cards remain readable | Browser | Manual | P1 | C | `site.css`, shared layout, Home/Auth views |
| A-012 | Production text hygiene | Anonymous | App running | Scan rendered anonymous HTML | No user-visible `local demos`, `local testing`, `demo accounts`, `Stripe is not configured`, or `test secret key` | Rendered pages | Automated | P0 | B | Shared views, auth views, customer views |

## B. Customer

| Test ID | Area | Role | Preconditions | Steps | Expected result | Data required | Automation feasibility | Priority | Batch assignment | Files likely involved |
|---|---|---|---|---|---|---|---|---|---|---|
| B-001 | Login/logout | Customer | Customer account exists | Login, verify greeting, logout | Auth state changes correctly, nav stays readable | Customer credentials | Hybrid | P0 | C | `_LoginPartial.cshtml`, Identity pages |
| B-002 | Detail add form | Customer | Logged in, product in stock | Open detail, set quantity | Quantity field binds to `ShoppingCart.Count` | Product id | Hybrid | P0 | C | `Details.cshtml`, `CartController` |
| B-003 | Add to cart | Customer | Logged in, product in stock | Submit Add to Cart | Cart item is created/updated, toast displays, cart count changes | Product id | Hybrid | P0 | C | `Details.cshtml`, `CartController`, ShoppingCart component |
| B-004 | Cart index | Customer | Cart has items | Open `/Customer/Cart/Index` | Items, quantities, line totals, and total render | Cart rows | Hybrid | P0 | C | `Cart/Index.cshtml`, `CartController` |
| B-005 | Cart plus | Customer | Cart item exists | Click Plus | Count increments by one, totals update, route uses `cartId` | Cart item | Manual | P0 | C | `Cart/Index.cshtml`, `CartController` |
| B-006 | Cart minus | Customer | Cart item count greater than 1 | Click Minus | Count decrements or item is removed according to current behavior | Cart item | Manual | P0 | C | `Cart/Index.cshtml`, `CartController` |
| B-007 | Cart remove | Customer | Cart item exists | Click Remove | Item is removed, count and totals update | Cart item | Manual | P0 | C | `Cart/Index.cshtml`, `CartController` |
| B-008 | Empty cart | Customer | Cart can be emptied | Remove all items, open cart | Empty state renders and summary is not reachable unexpectedly | Customer session | Hybrid | P1 | C | `Cart/Index.cshtml`, `CartController` |
| B-009 | Cart summary | Customer | Cart has items | Open `/Customer/Cart/Summary` | Shipping fields and order summary render | Cart rows, user address | Hybrid | P0 | C | `Cart/Summary.cshtml`, `CartController` |
| B-010 | Shipping validation | Customer | Cart has items | Submit summary with missing required fields | Validation messages render, no order is created | Cart rows | Manual | P0 | C | `Summary.cshtml`, `CartController` |
| B-011 | Place order | Customer | Cart has valid items | Submit order with valid shipping | OrderHeader and OrderDetail records are created; redirect/payment behavior follows configuration | Cart rows, Stripe config or fallback | Manual | P0 | C | `CartController`, `OrderHeader`, `OrderDetail` |
| B-012 | Order confirmation | Customer | Order placed | Open valid confirmation URL | Order number displays, no fake tracking/actions appear | Order id | Hybrid | P1 | C | `Orderconfirmation.cshtml`, `CartController` |
| B-013 | Payment fallback | Customer | Stripe unavailable or configured test mode | Place order under documented local conditions | User-facing message is professional and backend state is consistent | Stripe/local config | Manual | P1 | C | `CartController`, `Summary.cshtml` |
| B-014 | Customer responsive UI | Customer | Logged in | Check detail/cart/summary at 1440, 768, 390 widths | Forms, totals, cart actions, and nav remain usable | Browser | Manual | P1 | C | Customer Razor views, `site.css` |

## C. Admin / Employee

| Test ID | Area | Role | Preconditions | Steps | Expected result | Data required | Automation feasibility | Priority | Batch assignment | Files likely involved |
|---|---|---|---|---|---|---|---|---|---|---|
| C-001 | Admin login | Admin | Admin account exists | Login as admin | Admin nav and management links are visible | Admin credentials | Hybrid | P0 | D | `_Layout.cshtml`, Identity |
| C-002 | Product list | Admin/Employee | Authorized user | Open `/Admin/Product/Index` | Page returns 200 and `#tblData` exists | Products | Hybrid | P0 | D | `Admin/Views/Product/Index.cshtml`, `wwwroot/js/product.js` |
| C-003 | Product DataTables load | Admin/Employee | Authorized user | Wait for table AJAX | Rows load from `/Admin/Product/GetAll` | Products | Hybrid | P0 | D | `ProductController`, `product.js` |
| C-004 | Product DataTables controls | Admin/Employee | Rows loaded | Search, sort, paginate | Controls work without console errors | Products | Manual | P1 | D | `product.js`, `site.css` |
| C-005 | Product create | Admin | Authorized user | Open Upsert, fill required fields, upload image | Product creates with correct fields and image path | Reversible product data | Manual | P0 | D | `Product/Upsert.cshtml`, `ProductController` |
| C-006 | Product edit | Admin | Product exists | Open Upsert with id, edit non-destructive field | Existing values render; update persists | Product id | Manual | P0 | D | `Upsert.cshtml`, `ProductController` |
| C-007 | Product delete confirmation | Admin | Reversible product exists | Click Delete and confirm/cancel paths | SweetAlert confirmation works; delete endpoint behavior is correct | Reversible product | Manual | P1 | D | `product.js`, `ProductController` |
| C-008 | Image preview/upload | Admin | Create/Edit page open | Select image file | `#uploadBox` validation/preview behavior remains intact | Local image | Manual | P1 | D | `Upsert.cshtml`, `product.js` |
| C-009 | Category CRUD | Admin | Category pages present | Create/edit/delete category with reversible data | CRUD works and validation displays | Reversible category | Manual | P1 | D | Admin Category views/controllers |
| C-010 | CoverType CRUD | Admin | CoverType pages present | Create/edit/delete cover type | CRUD works and validation displays | Reversible cover type | Manual | P1 | D | Admin CoverType views/controllers |
| C-011 | Company CRUD | Admin | Company pages present | Create/edit/delete company | CRUD works and validation displays | Reversible company | Manual | P1 | D | Admin Company views/controllers |
| C-012 | Order list | Admin/Employee | Orders exist | Open `/Admin/Order/Index` | Page returns 200 and `#tblData` exists | Orders | Hybrid | P0 | D | `Admin/Views/Order/Index.cshtml`, `order.js` |
| C-013 | Order status filters | Admin/Employee | Orders in statuses exist | Click each status filter | `asp-route-status` values drive correct table endpoint | Orders | Hybrid | P0 | D | `Order/Index.cshtml`, `order.js` |
| C-014 | Order details actions | Admin/Employee | Order exists | Open details and inspect available actions by status | Buttons match current business rules; no invented statuses appear | Order id | Manual | P0 | D | `Order/Details.cshtml`, `OrderController` |
| C-015 | Role-based access | Admin/Employee/Customer | Users in each role exist | Attempt admin URLs with each role | Access matches authorization requirements | User accounts | Hybrid | P0 | E | Controllers, Identity, `SD` roles |

## D. Database / Data Integrity

| Test ID | Area | Role | Preconditions | Steps | Expected result | Data required | Automation feasibility | Priority | Batch assignment | Files likely involved |
|---|---|---|---|---|---|---|---|---|---|---|
| D-001 | Seed stability | Developer | Development DB available | Start app with seeding enabled in local-safe DB | Seed operation is idempotent and does not duplicate rows | Local DB | Hybrid | P1 | C | `DemoDataSeeder.cs`, `DbInitializer.cs` |
| D-002 | Modern product images | Developer | Products exist | Query/render product image paths | Active products use `/images/products/book-covers-modern/*.png` where expected | Products | Automated | P0 | B | `DemoDataSeeder.cs`, Home views, static assets |
| D-003 | No stale SVG paths | Developer | App running | Scan rendered catalog/detail HTML | No `/images/products/book-covers/*.svg` path is rendered | Products | Automated | P0 | B | Product data, Home views |
| D-004 | Cart line totals | Customer | Cart has varied quantities | Compare rendered line totals to server price tiers | Totals match `GetPriceBasedOnQuantity` behavior | Cart rows/products | Hybrid | P0 | C | `CartController`, `Product` |
| D-005 | Order total | Customer/Admin | Order exists | Compare OrderHeader total to OrderDetail lines | Header total equals sum of detail lines | Orders | Hybrid | P0 | C | `OrderHeader`, `OrderDetail`, controllers |
| D-006 | Order consistency | Admin | Orders exist | Inspect OrderHeader/OrderDetail relationships | Each detail points to valid order and product | Orders/products | Hybrid | P0 | C | DataAccess, Models |
| D-007 | Stock display | Anonymous/Customer | Product stock values include zero and positive | Open catalog/detail | Stock UI reflects quantity without blocking unrelated rendering | Product data | Manual | P1 | C | Home views, `Product.StockQuantity` |
| D-008 | Category/Cover relations | Admin | Products/categories/cover types exist | Filter and edit product category/cover | Relationships stay valid and selects use `asp-items` | Product/category/cover data | Manual | P1 | D | `ProductVM`, Upsert view |
| D-009 | Cascade delete safety | Admin | Reversible data or DB backup | Attempt deletion paths in controlled DB | Deleting parent data does not unintentionally orphan critical rows | Backup/local DB | Manual | P1 | D | EF model configuration, controllers |
| D-010 | Migration safety | Developer | Pending migrations may exist | Start app with `Database__AutoMigrate=false` | App logs warning rather than applying migrations automatically | Local DB | Manual | P1 | F | `DbInitializer.cs`, deployment config |

## E. Security

| Test ID | Area | Role | Preconditions | Steps | Expected result | Data required | Automation feasibility | Priority | Batch assignment | Files likely involved |
|---|---|---|---|---|---|---|---|---|---|---|
| E-001 | CSRF coverage | Customer/Admin | Authenticated session | Inspect/submit POST forms | Anti-forgery tokens exist and invalid tokens are rejected | Auth sessions | Hybrid | P0 | E | Razor forms, controllers |
| E-002 | Unsafe GET mutations | Anonymous/Auth | Routes known | Review controllers for state-changing GET actions | Mutations require POST or documented safe exception | Code review | Manual | P0 | E | Controllers |
| E-003 | Role restrictions | Users in all roles | Auth sessions | Open protected admin/customer actions | Unauthorized users get redirect/access denied | User accounts | Hybrid | P0 | E | Controllers, Identity config |
| E-004 | IDOR ownership | Customer A/B | Two customers with orders/carts | Customer A requests Customer B resources by id | Access is denied or scoped to owner | Two accounts | Manual | P0 | E | Cart/Order controllers |
| E-005 | XSS in descriptions | Admin/Anonymous | Product descriptions can contain HTML | Add safe and unsafe HTML in controlled DB | Sanitization/encoding behavior is known and safe | Reversible product | Manual | P0 | E | `Details.cshtml`, product admin |
| E-006 | File upload validation | Admin | Upload available | Try invalid file extensions and large files | Invalid uploads are rejected and paths are safe | Test files | Manual | P0 | E | `ProductController`, `Upsert.cshtml` |
| E-007 | Path traversal | Admin | Upload available | Attempt file names with traversal sequences | App does not write outside intended image folder | Test file | Manual | P0 | E | `ProductController` |
| E-008 | Over-posting | Customer/Admin | Forms known | Submit extra fields using HTTP client | Server ignores or rejects unauthorized fields | Auth session | Hybrid | P1 | E | Controllers, view models |
| E-009 | Secrets leakage | Anonymous | App running | Scan rendered HTML and repo publish artifacts | No connection strings/API keys in HTML/static assets | Config/publish output | Hybrid | P0 | E | appsettings, views, publish files |
| E-010 | Stripe config handling | Customer/Admin | Stripe disabled/enabled scenarios | Exercise payment paths in controlled config | User messages are safe; internal logs do not leak secrets | Local config | Manual | P1 | E | Cart/Order controllers |
| E-011 | Facebook auth config | Anonymous | Facebook keys absent/present | Open login/register | External provider section handles config safely | Config | Manual | P2 | E | Program.cs, Identity pages |
| E-012 | Email config handling | Customer/Admin | Email sender configured or absent | Trigger email-related flows | No secret leakage; failures are controlled | Config | Manual | P2 | E | `EmailSender`, Identity |

## F. UI / UX

| Test ID | Area | Role | Preconditions | Steps | Expected result | Data required | Automation feasibility | Priority | Batch assignment | Files likely involved |
|---|---|---|---|---|---|---|---|---|---|---|
| F-001 | Navbar | All | App running | Check anonymous, customer, admin states | Links, dropdown, cart, greeting, logout remain aligned | Auth sessions | Manual | P0 | C/D | `_Layout.cshtml`, `_LoginPartial.cshtml` |
| F-002 | Footer | All | App running | Inspect footer across key pages | Footer does not overlap content and copy is production-safe | Pages | Manual | P1 | C | `_Layout.cshtml`, `site.css` |
| F-003 | Home hero | Anonymous | Products exist | Open Home desktop/mobile | Hero text, CTAs, and stats fit without clipping | Products | Manual | P1 | C | `Index.cshtml`, `site.css` |
| F-004 | Filter panel | Anonymous | Products/categories exist | Use filters on desktop/mobile | Controls stack/read correctly and submit remains obvious | Data | Hybrid | P1 | C | `Index.cshtml`, `site.css` |
| F-005 | Product cards | Anonymous | Products exist | Inspect cards with long titles/zero stock/missing image | Cards remain aligned and readable | Products | Manual | P1 | C | `Index.cshtml`, `site.css` |
| F-006 | Pagination | Anonymous | Multiple pages exist | Open page 2 | Active state, disabled states, and query links are readable | Products | Automated | P0 | B | `Index.cshtml`, `site.css` |
| F-007 | Detail page | Customer/Anonymous | Product exists | Inspect detail at desktop/mobile | Cover, metadata, tiers, quantity, CTA layout are usable | Product | Manual | P1 | C | `Details.cshtml`, `site.css` |
| F-008 | Cart page | Customer | Cart has items | Inspect cart at desktop/mobile | Quantity buttons, remove action, totals, and CTA are clear | Cart rows | Manual | P1 | C | `Cart/Index.cshtml`, `site.css` |
| F-009 | Checkout page | Customer | Cart has items | Inspect summary at desktop/mobile | Shipping form and order summary are readable | Cart rows | Manual | P1 | C | `Cart/Summary.cshtml`, `site.css` |
| F-010 | Admin tables | Admin | DataTables pages available | Inspect product/order tables | Controls and row actions are dense but readable | Admin data | Manual | P1 | D | Admin views, `site.css` |
| F-011 | Admin forms | Admin | Upsert/detail pages available | Inspect product/order forms | Grouping, validation, upload/status/action areas are clear | Admin data | Manual | P1 | D | Admin views, `site.css` |
| F-012 | Auth pages | Anonymous | Identity enabled | Inspect login/register | Forms feel secure, validation is readable, no demo copy | None | Automated | P0 | B | Login/Register views, `site.css` |
| F-013 | Confirmation pages | Customer/Admin | Valid order id | Open confirmation pages | Success state is polished and order number is visible | Order id | Manual | P2 | C/D | Confirmation views |
| F-014 | Toasts/alerts | All | Trigger TempData messages | Inspect success/error/info/warning | Toastr shows once, text is readable, no JS error | Safe actions | Hybrid | P0 | B/C/D | `_Notification.cshtml`, `site.css` |

## G. Deployment Readiness

| Test ID | Area | Role | Preconditions | Steps | Expected result | Data required | Automation feasibility | Priority | Batch assignment | Files likely involved |
|---|---|---|---|---|---|---|---|---|---|---|
| G-001 | Publish safety | Developer | Publish profile or target known | Review publish output/config | No development-only secrets or local-only copy is shipped | Publish output | Hybrid | P0 | F | `DEPLOYMENT_SOMEE.md`, publish folders |
| G-002 | Production config | Developer | Deployment environment known | Verify environment variables override appsettings | Connection string, Stripe, Facebook, email config come from environment/host | Host config | Manual | P0 | F | appsettings, hosting config |
| G-003 | Connection string | Developer | Deployment DB exists | Start app against deployment-like config | App connects without auto-migration surprises | DB/config | Manual | P0 | F | `Program.cs`, `DbInitializer.cs` |
| G-004 | Stripe/Facebook/email keys | Developer | Keys available or intentionally absent | Check app startup/login/payment behavior | Missing keys disable optional features safely; present keys work | Config | Manual | P1 | F | Program, Utility services |
| G-005 | Static assets | Anonymous | Published app running | Request CSS/JS/images | Assets return 200 with correct paths | Published app | Automated | P0 | F | `wwwroot` |
| G-006 | Somee/IIS hosting | Developer | Somee or IIS target available | Deploy package and open app | App starts, HTTPS/static files/routing work | Host | Manual | P0 | F | publish package, web.config |
| G-007 | No local/demo/test visible text | Anonymous | Published app running | Smoke rendered pages | No development-only user-visible copy | Published app | Automated | P0 | F | Views/controllers |
| G-008 | Build/publish smoke | Developer | Clean checkout | Run `dotnet build` and publish command | Build/publish succeeds with expected warnings only | Source | Automated | P0 | F | Solution/projects |
