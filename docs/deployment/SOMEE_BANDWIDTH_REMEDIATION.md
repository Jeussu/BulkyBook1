# Somee bandwidth remediation

The October 2026 remediation keeps the existing .NET 8 / SQL Server schema and free Somee host. It reduces image response bytes; actual provider-policy compliance still requires evidence over a completed Somee monitoring window.

## Image contract

- Originals live outside the web root in `assets/product-images/originals` and are not published.
- `tools/BulkyBook.AssetTool` creates 160/320/600 bounding-width renditions in JPEG and WebP, without cropping or upscaling. Thumbnail height is bounded to 1.5 times its width budget. Encoder quality is 88, 82, or 76; processing rejects an image that cannot meet its budget at that quality floor.
- Small/thumbnail responses are at most 50,000 bytes; detail responses are at most 100,000 bytes. Generated bundled filenames contain the SHA-256 prefix of their actual encoded bytes.
- `App_Data/product-image-manifest.json` maps 61 historical image URLs, including legacy JPGs and SVGs, to optimized images. Old URLs return 308 to a correctly named JPEG. Database records do not need rewriting.
- Razor's `product-image` helper emits WebP with JPEG fallback, responsive dimensions, lazy loading for appropriate images, and a small missing-image fallback. Visible desktop hero images remain eager. Hidden mobile hero images select the tiny placeholder.
- Uploads require matching extension, MIME, signature and complete decode. Limits are 2 MiB input, 4096-pixel edges, 8 megapixels, and one frame. One upload processes at a time; busy requests fail safely after a bounded wait. Pixel limits bound native decoded allocations. EXIF orientation is applied and output metadata is stripped.
- Upload sets are staged outside the web root, published atomically, and assigned generated identifiers. Only application-owned sets are removed, after successful database persistence. Database failures remove the new set and retain the previous image. Historical files and unknown user files are not deleted.

## Dependency and hosting

SkiaSharp 4.153.1 is MIT licensed and supports .NET 8. It requires its native Windows runtime. License and native third-party notices ship in `ThirdPartyNotices`. The inspected Somee pool is 32 bit: publish for `win-x86`, framework-dependent, with `UseAppHost=false`. Local x86 tests exercise actual native encoding/decoding and upload failure cleanup. Do not substitute an x64 native DLL in that pool.

## Generate and validate

From the repository root:

```powershell
dotnet run --project tools/BulkyBook.AssetTool -c Release -- generate .
dotnet build BulkyBook.sln -c Release
dotnet test BulkyBook.Tests -c Release
dotnet test BulkyBook.Tests -c Release --arch x86
dotnet publish BulkyBookWeb -c Release -r win-x86 --self-contained false -o <new-empty-package-directory> -p:SourceRevisionId=<verified-full-git-sha>
powershell -NoProfile -File tools/qa/run-deployment-readiness.ps1 -SkipPublish -PublishDir <package-directory>
```

Against an isolated, running local copy of the package:

```powershell
dotnet run --project tools/BulkyBook.AssetTool -c Release -- http <package-directory> http://127.0.0.1:<qa-port>
```

HTTP QA is deliberately restricted to loopback. It verifies actual encoded body bytes, image MIME/ETags/immutable caching, zero-body conditional responses, text compression integrity, private-page no-store behavior, source-map denial, and historical URL compatibility. It does not establish Somee compliance.

## Caching and package hygiene

Content-hashed bundled images and generated immutable upload URLs receive one-year public immutable caching. Other public static assets use one hour, or one year when versioned. CSS and application JavaScript references use content-version query strings. Compression is scoped to public static text; account/customer HTML and JSON do not enter that compression branch or receive public static cache headers.

The package excludes source maps (including Identity/RCL maps), PDBs, raw historical raster originals, local upload directories, protected upload metadata/staging, and unminified library sources. Retain minified library dependencies and the compatibility manifest. The explicit uncompressed-text exceptions are the existing theme (270,000-byte cap) and Identity Bootstrap minified CSS / RTL CSS (165,000-byte caps); HTTP QA separately requires compressed theme transfer below 150,000 bytes. There are no oversized-image exceptions.

## Deployment and rollback gates

1. Verify the exact Somee account, website and root, pool bitness, runtime support, suspension state, restart allowance and free space.
2. Obtain a recoverable off-host backup of affected application files and configuration before modifying hosting. Preserve uploads separately, including existing legacy uploads and new optimized upload sets/metadata. Inventory deployed paths and hashes. Never commit live configuration or credentials.
3. Inspect active product URLs and public image inventory. All active URLs must resolve safely. Unknown production uploads must be preserved; optimize/map them offline if necessary. The application preserves safe unknown existing image paths rather than assuming they are unused. Do not assume the development seed represents production.
4. Keep the database/schema unchanged. No migration or reseeding is part of this release. Any separate database mutation requires an independent recoverable database backup first.
5. Publish the exact integrated source revision into a new directory. `/health/release` exposes its assembly informational version for verification. Validate the package before adding protected hosting configuration.
6. Build a deployment copy that preserves host-specific `web.config`, environment variables and production connection settings. Never upload portable empty configuration over live secrets blindly. The readiness scanner is intended for the portable package and never needs live credential values.
7. Use provider-supported file management. When deployment is permitted, a controlled `app_offline.htm` maintenance interval may prevent mixed assemblies while files are replaced. Upload only reviewed release paths. Preserve all live uploads and unrelated files. Do not blanket-sync-delete.
8. Do not delete historical deployed images merely because source originals moved. Compatibility redirects prevent these known URLs from delivering their raw bodies once the new application is active. Any later cleanup requires a backed-up, verified exact-path manifest.
9. Verify uploaded integrity where available. Prefer automatic recovery from suspension. At most one manual website start is authorized after successful deployment, backup verification and confirmation of remaining allowance. Never start merely to inspect whether suspension has expired.
10. Perform a bounded public/read-only smoke check and inspect `/health/release`. Verify representative image, CSS and login headers. Avoid real emails, payments and unintended orders. Record `LOCAL_BANDWIDTH_BUDGET_PASS`, `LIVE_ASSET_TRANSFER_PASS`, and provider compliance separately.
11. If a material release regression occurs, restore only the backed-up affected application/configuration paths while preserving newer uploads and data. Do not roll back solely because a provider suspension still returns 404.

If File Manager/authentication/backup access is unavailable, stop before live modification. Retain the verified package and source revision for the next authorized continuation.

## Local evidence from October 25

Starting source: `a3cb4c22ccb60bccf15239c1bdbe554f3ea9de6f`.

An isolated SQL LocalDB database and production-mode x86 package were used; no production database was used. A local recording reverse proxy observed browser-originated requests at a 1280x900 viewport, preserving cache/compression headers and counting encoded response-body bytes. External CDNs, HTTP headers and TCP/TLS overhead are excluded.

The representative cold homepage decreased from 3,891,685 encoded first-party body bytes across 15 responses to approximately 198 KB across 15 responses (about 94.9% reduction). Responses above 150,000 bytes decreased from five to zero in that sample. Warm reloads requested only HTML: the additional responsive markup makes the optimized HTML somewhat larger. These are local measurements, not production telemetry or a hosting guarantee.
