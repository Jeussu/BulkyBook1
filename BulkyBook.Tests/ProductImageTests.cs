using System.Text.Json;
using BulkyBook.Utility;
using BulkyBookWeb.Services;
using BulkyBookWeb.TagHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using System.Reflection;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBookWeb.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Encodings.Web;

namespace BulkyBook.Tests;

public sealed class ProductImageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BulkyBookImageTests-" + Guid.NewGuid().ToString("N"));
    private readonly ProductImageStore _store;

    public ProductImageTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "wwwroot"));
        Directory.CreateDirectory(Path.Combine(_root, "App_Data"));
        File.WriteAllText(Path.Combine(_root, "App_Data", "product-image-manifest.json"), "{}");
        _store = new ProductImageStore(new EnvironmentStub(_root), NullLogger<ProductImageStore>.Instance);
    }

    [Fact]
    public void EncodesReadableBoundedImagesWithMatchingSignatures()
    {
        var outputs = ProductImageProcessor.Process(Png(600, 900), ".png");
        Assert.Equal(6, outputs.Count);
        foreach (var item in outputs)
        {
            Assert.InRange(item.Data.Length, 1, item.Size == 600 ? 100_000 : 50_000);
            using var data = SKData.CreateCopy(item.Data);
            using var codec = SKCodec.Create(data);
            Assert.Equal(item.Extension == ".jpg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Webp, codec.EncodedFormat);
            Assert.Equal(item.Width, codec.Info.Width);
            Assert.Equal(item.Height, codec.Info.Height);
            Assert.Equal(2d / 3, item.Width / (double)item.Height, 2);
        }
    }

    [Fact]
    public void RejectsSignatureMismatchAndNonImageInput()
    {
        Assert.Throws<InvalidDataException>(() => ProductImageProcessor.Process(Png(32, 32), ".jpg"));
        Assert.Throws<InvalidDataException>(() => ProductImageProcessor.Process("<script>bad</script>"u8.ToArray(), ".png"));
        Assert.Throws<InvalidDataException>(() => ProductImageProcessor.Process(Png(32, 32), ".svg"));
    }

    [Fact]
    public void RejectsByteAndPixelBombsBeforeFullDecode()
    {
        Assert.Throws<InvalidDataException>(() => ProductImageProcessor.Process(new byte[ProductImageProcessor.MaxUploadBytes + 1], ".png"));
        Assert.Throws<InvalidDataException>(() => ProductImageProcessor.Process(Png(4097, 1), ".png"));
        Assert.Throws<InvalidDataException>(() => ProductImageProcessor.Process(Png(3000, 3000), ".png"));
    }

    [Fact]
    public void RejectsAnimatedInput()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "animated.gif"));
        Assert.Throws<InvalidDataException>(() => ProductImageProcessor.Process(bytes, ".gif"));
    }

    [Fact]
    public void AppliesExifOrientationAndStripsMetadata()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oriented.jpg"));
        var outputs = ProductImageProcessor.Process(bytes, ".jpg");
        Assert.All(outputs, image => { Assert.Equal(64, image.Width); Assert.Equal(96, image.Height); });
        using var data = SKData.CreateCopy(outputs[0].Data);
        using var codec = SKCodec.Create(data);
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
    }

    [Fact]
    public void AcceptsWebpAndStaticGifWithoutUpscaling()
    {
        var webp = ProductImageProcessor.Process(Png(64, 96), ".png").First(image => image.Extension == ".webp").Data;
        Assert.All(ProductImageProcessor.Process(webp, ".webp"), image => Assert.InRange(image.Width, 1, 64));
        var gif = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "static.gif"));
        Assert.All(ProductImageProcessor.Process(gif, ".gif"), image => Assert.Equal(8, image.Width));
    }

    [Fact]
    public void MissingImageFallsBackAndAltTextIsEncoded()
    {
        var helper = new ProductImageTagHelper(_store) { ImageUrl = "/images/products/missing.jpg", Alt = "<script>alert(1)</script>" };
        var context = new TagHelperContext(new TagHelperAttributeList(), new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("product-image", new TagHelperAttributeList(), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.Process(context, output);
        using var writer = new StringWriter(); output.WriteTo(writer, HtmlEncoder.Default);
        var html = writer.ToString();
        Assert.Contains(ProductImageStore.Placeholder, html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("loading=\"lazy\"", html);
    }

    [Theory]
    [InlineData("https://evil.example/image.jpg")]
    [InlineData("//evil.example/image.jpg")]
    [InlineData("/images/products/../../appsettings.json")]
    [InlineData("/images/products/%2e%2e/file.jpg")]
    [InlineData("/images/products/file.jpg?secret=value")]
    [InlineData("/images/products/file.jpg:stream")]
    public void RejectsUnsafeImagePaths(string path)
    {
        Assert.Null(ProductImageStore.Normalize(path));
        Assert.Equal(ProductImageStore.Placeholder, _store.Fallback(path));
        _store.DeleteOwned(path);
    }

    [Fact]
    public void NormalizesHistoricalWindowsPaths()
        => Assert.Equal("/images/products/a.jpg", ProductImageStore.Normalize(@"\images\products\a.jpg"));

    [Fact]
    public async Task UploadProducesCompleteVariantsAndDeletesOnlyOwnedFiles()
    {
        var bytes = Png(600, 900);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "../../evil.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        var url = await _store.SaveAsync(file, CancellationToken.None);
        Assert.StartsWith("/images/products/optimized/uploads/", url);
        Assert.DoesNotContain("evil", url);
        var images = _store.Resolve(url);
        Assert.NotNull(images);
        Assert.True(File.Exists(Physical(images.DetailJpeg.Url)));
        Assert.True(File.Exists(Physical(images.ThumbnailWebp.Url)));
        var directory = Path.GetDirectoryName(Physical(url))!;
        File.WriteAllText(Path.Combine(directory, "preserve.txt"), "unrelated");
        _store.DeleteOwned(url);
        Assert.False(File.Exists(Physical(url)));
        Assert.True(File.Exists(Path.Combine(directory, "preserve.txt")));
        Assert.Null(_store.Resolve(url));
    }

    [Fact]
    public async Task RejectedUploadLeavesNoPublicOrMetadataFiles()
    {
        var bytes = Png(32, 32);
        var mismatched = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "cover.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.SaveAsync(mismatched, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "wwwroot"), "*", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "App_Data"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancellationNeverPublishesPartialSet()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var bytes = Png(32, 32);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "cover.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.SaveAsync(file, cancellation.Token));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "wwwroot"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void LegacyAndArbitraryFilesAreNeverDeletedByUploadCleanup()
    {
        var directory = Path.Combine(_root, "wwwroot", "images", "products"); Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "old.jpg"); File.WriteAllBytes(original, Png(10, 10));
        _store.DeleteOwned(@"\images\products\old.jpg");
        Assert.True(File.Exists(original));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DatabaseFailurePreservesOldImageAndCleansNewSet(bool failDuringUpdate)
    {
        var original = Path.Combine(_root, "wwwroot", "images", "products", "old.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        File.WriteAllBytes(original, Png(10, 10));
        var productRepository = DispatchProxy.Create<IProductRepository, InterfaceStub>();
        ((InterfaceStub)(object)productRepository).Handler = (method, _) =>
        {
            if (method.Name == "GetFirstOrDefault") return new Product { Id = 1, ImageUrl = "/images/products/old.jpg" };
            if (method.Name == "Update" && failDuringUpdate) throw new InvalidOperationException("Simulated database failure");
            return null;
        };
        var unit = DispatchProxy.Create<IUnitOfWork, InterfaceStub>();
        ((InterfaceStub)(object)unit).Handler = (method, _) => method.Name == "get_Product" ? productRepository
            : throw new InvalidOperationException("Simulated database failure");
        var controller = new ProductController(unit, NullLogger<ProductController>.Instance, _store)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var bytes = Png(64, 96);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "cover.png")
        { Headers = new HeaderDictionary(), ContentType = "image/png" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Upsert(
            new ProductVM { Product = new Product { Id = 1, Description = "Safe description" } }, file));
        Assert.True(File.Exists(original));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "wwwroot"), "*", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "App_Data"), "*", SearchOption.AllDirectories));
    }

    public class InterfaceStub : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    private string Physical(string url) => Path.Combine(_root, "wwwroot", url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        // Only the fixture's own unique temp root can be removed.
        if (Path.GetFileName(_root).StartsWith("BulkyBookImageTests-", StringComparison.Ordinal)) Directory.Delete(_root, recursive: true);
    }

    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "BulkyBook.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
