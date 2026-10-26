using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using BulkyBook.Utility;
using SkiaSharp;

if (args.Length < 2 || args[0] is not ("generate" or "validate" or "http"))
    throw new ArgumentException("Usage: AssetTool generate <repository-root> | validate <publish-root> | http <publish-root> <loopback-base-url>");
var root = Path.GetFullPath(args[1]);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
if (args[0] == "generate")
{
    var originals = Path.Combine(root, "assets", "product-images", "originals");
    if (!File.Exists(Path.Combine(root, "BulkyBook.sln"))) throw new InvalidOperationException("Generate must run against the repository, never a live deployment root.");
    var web = Path.Combine(root, "BulkyBookWeb", "wwwroot");
    var output = Path.Combine(web, "images", "products", "optimized", "bundled");
    Directory.CreateDirectory(output);
    var manifest = new SortedDictionary<string, ProductImageSet>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in Directory.EnumerateFiles(originals, "*", SearchOption.AllDirectories).Order())
    {
        if (!new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif" }.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
        var input = File.ReadAllBytes(file);
        var encoded = ProductImageProcessor.Process(input, Path.GetExtension(file), trustedAsset: true);
        var items = new Dictionary<string, ImageRendition>();
        foreach (var rendition in encoded)
        {
            var hash = Convert.ToHexString(SHA256.HashData(rendition.Data)).ToLowerInvariant()[..24];
            var name = $"{hash}-{rendition.Size}{rendition.Extension}";
            File.WriteAllBytes(Path.Combine(output, name), rendition.Data);
            items[$"{rendition.Size}{rendition.Extension}"] = new($"/images/products/optimized/bundled/{name}", rendition.Width, rendition.Height, rendition.Data.Length);
        }
        var set = new ProductImageSet(items["160.jpg"], items["160.webp"], items["320.jpg"], items["320.webp"], items["600.jpg"], items["600.webp"]);
        var oldUrl = "/" + Path.GetRelativePath(originals, file).Replace('\\', '/');
        manifest.Add(oldUrl, set);
        if (oldUrl.Contains("/book-covers-modern/"))
            manifest.Add(oldUrl.Replace("/book-covers-modern/", "/book-covers/").Replace(".png", ".svg"), set);
        Console.WriteLine($"{oldUrl}: original={input.Length}, thumbnail={set.ThumbnailWebp.Bytes}, detail={set.DetailWebp.Bytes}");
    }
    var manifestFile = Path.Combine(root, "BulkyBookWeb", "App_Data", "product-image-manifest.json");
    Directory.CreateDirectory(Path.GetDirectoryName(manifestFile)!);
    File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest, jsonOptions));
    var referenced = manifest.Values.SelectMany(set => new[] { set.SmallJpeg, set.SmallWebp, set.ThumbnailJpeg, set.ThumbnailWebp, set.DetailJpeg, set.DetailWebp })
        .Select(item => Path.GetFileName(item.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    // This source directory contains generated bundled assets only. Never touch upload directories.
    foreach (var oldFile in Directory.EnumerateFiles(output))
        if (Regex.IsMatch(Path.GetFileName(oldFile), @"^[a-f0-9]{24}-(160|320|600)\.(jpg|webp)$") && !referenced.Contains(Path.GetFileName(oldFile))) File.Delete(oldFile);
    Console.WriteLine($"Generated {manifest.Count} compatibility mappings.");
}
else if (args[0] == "http")
{
    if (args.Length != 3 || !Uri.TryCreate(args[2], UriKind.Absolute, out var origin) || !origin.IsLoopback)
        throw new ArgumentException("HTTP QA is restricted to an explicitly supplied loopback application URL.");
    var manifest = JsonSerializer.Deserialize<Dictionary<string, ProductImageSet>>(File.ReadAllText(Path.Combine(root, "App_Data", "product-image-manifest.json")))!;
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None })
        { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BulkyBookLocalBandwidthQA/1.0");
    var images = manifest["/images/products/book-covers-modern/practical-devops-for-developers.png"];
    foreach (var (image, budget) in new[] { (images.ThumbnailWebp, 50_000), (images.DetailWebp, 100_000) })
    {
        using var response = await client.GetAsync(image.Url);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Require(response.IsSuccessStatusCode && bytes.Length == image.Bytes && bytes.Length <= budget, image.Url + " body budget/integrity");
        Require(response.Content.Headers.ContentType?.MediaType == "image/webp" && !response.Content.Headers.ContentEncoding.Any(), image.Url + " image MIME/encoding");
        Require(response.Headers.CacheControl?.ToString().Contains("immutable") == true && response.Headers.ETag is not null, image.Url + " versioned cache validators");
        using var conditional = new HttpRequestMessage(HttpMethod.Get, image.Url);
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var warm = await client.SendAsync(conditional);
        Require(warm.StatusCode == System.Net.HttpStatusCode.NotModified && (await warm.Content.ReadAsByteArrayAsync()).Length == 0, image.Url + " conditional body=0");
        Console.WriteLine($"TRANSFER {image.Url}: cold_body={bytes.Length}, conditional_body=0, cache={response.Headers.CacheControl}");
    }
    using (var response = await client.GetAsync("/css/bootswatchTheme.css"))
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Require(response.IsSuccessStatusCode && response.Content.Headers.ContentEncoding.Contains("gzip") && bytes.Length < 150_000, "CSS compressed transfer budget");
        using var input = new MemoryStream(bytes); using var gzip = new GZipStream(input, CompressionMode.Decompress); using var decoded = new MemoryStream();
        await gzip.CopyToAsync(decoded);
        Require(SHA256.HashData(decoded.ToArray()).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "wwwroot", "css", "bootswatchTheme.css")))), "CSS compression integrity");
        Console.WriteLine($"TRANSFER /css/bootswatchTheme.css: encoded_body={bytes.Length}, decoded_body={decoded.Length}");
    }
    using (var privatePage = await client.GetAsync("/Identity/Account/Login"))
        Require(privatePage.IsSuccessStatusCode && privatePage.Headers.CacheControl?.NoStore == true && !privatePage.Content.Headers.ContentEncoding.Any(), "Identity response has no-store and no static compression");
    using (var map = await client.GetAsync("/lib/jquery/dist/jquery.min.map"))
        Require(map.StatusCode == System.Net.HttpStatusCode.NotFound, "Source maps unavailable");
    using (var compatibility = await client.GetAsync("/images/products/book-covers-modern/practical-devops-for-developers.png"))
        Require((int)compatibility.StatusCode == 308 && compatibility.Headers.Location?.ToString() == images.DetailJpeg.Url, "Historical URL compatibility redirect");
    Console.WriteLine("LOCAL_HTTP_DELIVERY_PASS (local measurements do not establish Somee policy compliance)");
}
else
{
    var failures = new List<string>();
    var web = Path.Combine(root, "wwwroot");
    var manifestFile = Path.Combine(root, "App_Data", "product-image-manifest.json");
    var manifest = JsonSerializer.Deserialize<Dictionary<string, ProductImageSet>>(File.ReadAllText(manifestFile))!;
    foreach (var (url, set) in manifest)
    {
        foreach (var (item, size, budget) in new[] { (set.SmallJpeg, 160, 50_000), (set.SmallWebp, 160, 50_000),
                     (set.ThumbnailJpeg, 320, 50_000), (set.ThumbnailWebp, 320, 50_000), (set.DetailJpeg, 600, 100_000), (set.DetailWebp, 600, 100_000) })
        {
            var file = Path.GetFullPath(Path.Combine(web, item.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(web + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            { failures.Add($"{url}: missing or invalid rendition {item.Url}"); continue; }
            using var codec = SKCodec.Create(file);
            var expectedFormat = file.EndsWith(".webp") ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Jpeg;
            if (codec is null || codec.EncodedFormat != expectedFormat || codec.Info.Width != item.Width || codec.Info.Height != item.Height ||
                item.Width > size || item.Height > size * 1.5 || codec.FrameCount > 1)
                failures.Add($"{item.Url}: invalid format/dimensions/frames");
            else
            {
                using var decoded = new SKBitmap(codec.Info);
                if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success) failures.Add($"{item.Url}: invalid image payload");
            }
            if (new FileInfo(file).Length > budget || new FileInfo(file).Length != item.Bytes) failures.Add($"{item.Url}: size budget/manifest mismatch ({new FileInfo(file).Length} bytes, limit {budget})");
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()[..24];
            if (!Path.GetFileName(file).StartsWith(digest + "-", StringComparison.Ordinal)) failures.Add($"{item.Url}: content-hash mismatch; immutable caching would be unsafe");
        }
    }
    foreach (var file in Directory.EnumerateFiles(web, "*", SearchOption.AllDirectories))
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext == ".map") failures.Add($"{file}: source maps must not be published");
        if (new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".ico" }.Contains(ext) && new FileInfo(file).Length > 100_000)
            failures.Add($"{file}: public image exceeds 100,000 bytes");
        var relative = Path.GetRelativePath(web, file).Replace('\\', '/');
        // Explicit text exceptions; actual compressed transfers are checked by the HTTP smoke test.
        var textExceptions = new Dictionary<string, int>
        {
            ["css/bootswatchTheme.css"] = 270_000,
            ["Identity/lib/bootstrap/dist/css/bootstrap.min.css"] = 165_000,
            ["Identity/lib/bootstrap/dist/css/bootstrap.rtl.min.css"] = 165_000
        };
        if (new FileInfo(file).Length > 150_000 &&
            !(textExceptions.TryGetValue(relative, out var limit) && new FileInfo(file).Length <= limit))
            failures.Add($"{file}: unexpected oversized static file");
    }
    if (manifest.Count < 61) failures.Add("Compatibility manifest must retain all 61 known raster/SVG image URLs.");
    foreach (var symbol in Directory.EnumerateFiles(root, "*.pdb", SearchOption.AllDirectories)) failures.Add($"{symbol}: debug symbols must not be published");
    foreach (var message in failures) Console.Error.WriteLine("FAIL " + message);
    if (failures.Count != 0) return 1;
    Console.WriteLine($"LOCAL_BANDWIDTH_BUDGET_PASS: {manifest.Count} compatibility URLs; all generated images decode within budgets.");
}
return 0;

static void Require(bool passed, string name)
{
    if (!passed) throw new InvalidOperationException("FAIL " + name);
    Console.WriteLine("PASS " + name);
}
