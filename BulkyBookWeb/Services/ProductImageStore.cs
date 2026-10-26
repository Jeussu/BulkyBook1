using System.Text.Json;
using System.Text.RegularExpressions;
using BulkyBook.Utility;

namespace BulkyBookWeb.Services;

public sealed class ProductImageStore
{
    public const string Placeholder = "/images/product-placeholder.svg";
    private static readonly Regex UploadPath = new(@"^/images/products/optimized/uploads/([a-f0-9]{32})/600\.jpg$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ProductImageStore> _logger;
    private readonly Dictionary<string, ProductImageSet> _bundled;
    private readonly SemaphoreSlim _processing = new(1, 1);

    public ProductImageStore(IWebHostEnvironment environment, ILogger<ProductImageStore> logger)
    {
        _environment = environment;
        _logger = logger;
        var file = Path.Combine(environment.ContentRootPath, "App_Data", "product-image-manifest.json");
        _bundled = new(JsonSerializer.Deserialize<Dictionary<string, ProductImageSet>>(File.ReadAllText(file))!, StringComparer.OrdinalIgnoreCase);
    }

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var path = url.Replace('\\', '/');
        if (path.StartsWith("~/")) path = path[1..];
        if (!path.StartsWith('/') || path.StartsWith("//") || path.IndexOfAny(['%', '?', '#', ':', '\0']) >= 0 ||
            path.Split('/').Any(p => p is "." or "..")) return null;
        return path.StartsWith("/images/products/", StringComparison.OrdinalIgnoreCase) || path == "/images/lawn_leaf.jpg" ? path : null;
    }

    public ProductImageSet? Resolve(string? url)
    {
        var path = Normalize(url);
        if (path is null) return null;
        if (_bundled.TryGetValue(path, out var bundled)) return bundled;
        if (!UploadPath.IsMatch(path)) return null;
        var metadata = Path.Combine(_environment.ContentRootPath, "App_Data", "product-images", UploadPath.Match(path).Groups[1].Value + ".json");
        if (!File.Exists(metadata) || new FileInfo(metadata).Length > 10_000) return null;
        try { return JsonSerializer.Deserialize<ProductImageSet>(File.ReadAllText(metadata)); }
        catch (JsonException) { return null; }
    }

    public string Fallback(string? url)
    {
        // Preserve unknown existing uploads until deployment inspection can map them; never accept external/traversal URLs.
        var path = Normalize(url);
        return path is not null && File.Exists(PhysicalPath(path)) &&
            new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg" }.Contains(Path.GetExtension(path).ToLowerInvariant()) ? path : Placeholder;
    }

    public string? CompatibilityRedirect(string path) => _bundled.TryGetValue(path, out var images) ? images.DetailJpeg.Url : null;

    public async Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var mime = extension switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", _ => "" };
        if (mime.Length == 0 || !string.Equals(mime, file.ContentType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The image content type must match a supported file extension.");
        if (file.Length is <= 0 or > ProductImageProcessor.MaxUploadBytes)
            throw new InvalidDataException("Product image must be between 1 byte and 2 MiB.");
        if (!await _processing.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken))
            throw new InvalidDataException("Another image is being processed. Please try again.");
        var id = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(_environment.ContentRootPath, "App_Data", "image-staging", id);
        var destination = PhysicalPath($"/images/products/optimized/uploads/{id}");
        var metadata = Path.Combine(_environment.ContentRootPath, "App_Data", "product-images", id + ".json");
        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, cancellationToken)) != 0)
            {
                if (buffer.Length + count > ProductImageProcessor.MaxUploadBytes) throw new InvalidDataException("Image exceeds 2 MiB.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            }
            var encoded = ProductImageProcessor.Process(buffer.ToArray(), extension);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(stage);
            var items = new Dictionary<string, ImageRendition>();
            foreach (var item in encoded)
            {
                var filename = $"{item.Size}{item.Extension}";
                await File.WriteAllBytesAsync(Path.Combine(stage, filename), item.Data, cancellationToken);
                items[filename] = new($"/images/products/optimized/uploads/{id}/{filename}", item.Width, item.Height, item.Data.Length);
            }
            var images = new ProductImageSet(items["160.jpg"], items["160.webp"], items["320.jpg"], items["320.webp"], items["600.jpg"], items["600.webp"]);
            Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);
            await File.WriteAllTextAsync(metadata, JsonSerializer.Serialize(images), cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(stage, destination); // Publish a complete set atomically on this filesystem.
            return images.DetailJpeg.Url;
        }
        catch
        {
            DeleteOwned($"/images/products/optimized/uploads/{id}/600.jpg");
            DeleteGeneratedFiles(stage);
            throw;
        }
        finally { _processing.Release(); }
    }

    public void DeleteOwned(string? url)
    {
        var normalized = Normalize(url);
        if (normalized is null || !UploadPath.IsMatch(normalized)) return;
        var id = UploadPath.Match(normalized).Groups[1].Value;
        try
        {
            DeleteGeneratedFiles(PhysicalPath($"/images/products/optimized/uploads/{id}"));
            File.Delete(Path.Combine(_environment.ContentRootPath, "App_Data", "product-images", id + ".json"));
        }
        catch (IOException exception) { _logger.LogWarning(exception, "Unable to clean up generated product image set {ImageId}", id); }
        catch (UnauthorizedAccessException exception) { _logger.LogWarning(exception, "Unable to clean up generated product image set {ImageId}", id); }
    }

    private static void DeleteGeneratedFiles(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var size in new[] { 160, 320, 600 })
            foreach (var extension in new[] { ".jpg", ".webp" }) File.Delete(Path.Combine(directory, size + extension));
        // Leave unexpected files in place rather than recursively deleting user content.
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private string PhysicalPath(string url)
    {
        var root = Path.GetFullPath(_environment.WebRootPath) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid image path.");
        return path;
    }
}
