# BulkyBook Somee Deployment Test Cases

Use these cases for Somee/staging deployment. Do not run migrations or DB update commands unless a separate reviewed deployment plan explicitly approves them.

| ID | Priority | Role | Preconditions | Steps | Expected UI | Expected DB | Evidence / Screenshot | Pass / Fail | Related file / method | Fix needed |
|---|---|---|---|---|---|---|---|---|---|---|
| `SOMEE-01` | P0 | Developer/Operator | Somee site and SQL database exist. | Configure `ASPNETCORE_ENVIRONMENT=Production`, `ConnectionStrings__DefaultConnection`, `Database__AutoMigrate=false`, `SeedData__EnableDemoData=false`, `Application__BaseUrl`. | App starts on HTTPS domain. | Connects to intended DB only. | Sanitized Somee settings screenshots. | TBD | `Program.cs`, `SOMEE_ENVIRONMENT_VARIABLES.md` | Missing env var blocks deploy. |
| `SOMEE-02` | P0 | Developer/Operator | SMTP provider ready. | Set `Email__Provider=Smtp` and `Email__Smtp__*` environment variables. | Forgot/Resend pages do not show LocalFile notice. | Identity email side effects happen only after clicked links. | Sanitized config and inbox screenshots. | TBD | `EmailSender.cs` | No real email blocks production Identity. |
| `SOMEE-03` | P0 | Developer/Operator | Publish output created. | Verify publish output does not contain real secrets, local outbox files, or Development appsettings. | N/A | No DB change. | Secret scan output. | TBD | Publish folder | Remove/rotate any leaked secret. |
| `SOMEE-04` | P0 | Anonymous | App deployed. | Open Home, Details, Register/Login/Forgot Password. | Public pages load over HTTPS. | No DB change except registration if submitted. | Screenshots. | TBD | MVC/Identity routes | Fix launch/static asset/runtime errors. |
| `SOMEE-05` | P0 | Individual | Test user exists. | Login, add to cart, checkout with Stripe test keys or expected payment config. | Checkout completes or blocks with clear Stripe config error. | Order/cart/stock side effects match config. | Screenshots and DB query. | TBD | `CartController` | Payment/order issue blocks deploy. |
| `SOMEE-06` | P0 | Company | Company user exists. | Login, add to cart, submit delayed-payment checkout. | Confirmation page loads without Stripe. | `PaymentDueDate` set; cart clears; stock decreases. | Screenshot and DB query. | TBD | `CartController.SummaryPOST` | Company checkout issue blocks deploy. |
| `SOMEE-07` | P0 | Customer | Customer has order. | Open `My Orders`; attempt another user's detail ID. | Own orders visible; other user's order denied/not found. | No DB mutation. | Screenshots. | TBD | `OrderController` | Data leak blocks deploy. |
| `SOMEE-08` | P1 | Admin/Employee | Staff accounts exist. | Open Admin Orders; process/ship/cancel/refund test orders as allowed. | Staff workflow works by status. | Status/payment updates match action. | Screenshots and DB query. | TBD | `OrderController` | Staff workflow issue may block ops. |
| `SOMEE-09` | P1 | Admin | Admin account exists. | Create/edit product/category/company. | Admin CRUD works; image files render. | DB rows and uploaded files persist. | Screenshots. | TBD | Admin controllers/views | Admin CRUD issue blocks maintenance. |
| `SOMEE-10` | P1 | Developer/Operator | Logs accessible. | Review startup/runtime logs after smoke tests. | No unhandled exceptions. | No unintended migrations/resets/seeding. | Sanitized log excerpt. | TBD | Hosting logs | Fix recurring exceptions before launch. |

## Required Evidence

- Sanitized environment variable checklist.
- Release build output.
- Publish readiness scan output.
- Home/Product/Login/Forgot/Cart/Summary/My Orders/Admin Orders screenshots.
- DB query evidence for Identity, cart, latest order, and stock.
- SMTP inbox evidence without exposing full token URLs or secrets.
