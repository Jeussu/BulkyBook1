using SkiaSharp;

namespace BulkyBook.Utility;

public sealed record ImageRendition(string Url, int Width, int Height, int Bytes);
public sealed record ProductImageSet(ImageRendition SmallJpeg, ImageRendition SmallWebp,
    ImageRendition ThumbnailJpeg, ImageRendition ThumbnailWebp,
    ImageRendition DetailJpeg, ImageRendition DetailWebp);
public sealed record EncodedImageRendition(int Size, string Extension, int Width, int Height, byte[] Data);

/// <summary>Bounded, one-time processing. Never call this from a public image GET.</summary>
public static class ProductImageProcessor
{
    public const int MaxUploadBytes = 2 * 1024 * 1024;
    public const int ThumbnailBudget = 50_000;
    public const int DetailBudget = 100_000;
    public const string PipelineVersion = "v1";

    public static IReadOnlyList<EncodedImageRendition> Process(byte[] input, string extension, bool trustedAsset = false)
    {
        var maxBytes = trustedAsset ? 16 * 1024 * 1024 : MaxUploadBytes;
        if (input.Length == 0 || input.Length > maxBytes)
            throw new InvalidDataException("Product image must be between 1 byte and 2 MiB.");

        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("The file is not a supported image.");
        var expected = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".png" => SKEncodedImageFormat.Png,
            ".gif" => SKEncodedImageFormat.Gif,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => throw new InvalidDataException("Only JPEG, PNG, WebP, and non-animated GIF images are supported.")
        };
        if (codec.EncodedFormat != expected)
            throw new InvalidDataException("The image signature does not match its file extension.");
        var info = codec.Info;
        var maxEdge = trustedAsset ? 8192 : 4096;
        var maxPixels = trustedAsset ? 32_000_000L : 8_000_000L;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > maxEdge || info.Height > maxEdge ||
            (long)info.Width * info.Height > maxPixels)
            throw new InvalidDataException("Image dimensions exceed the 4096-pixel edge or 8-megapixel upload limit.");
        if (codec.FrameCount > 1)
            throw new InvalidDataException("Animated images are not supported. Please upload a single-frame cover.");

        using var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("The image is incomplete or cannot be decoded safely.");
        using var oriented = Orient(decoded, codec.EncodedOrigin);
        var result = new List<EncodedImageRendition>(6);
        foreach (var width in new[] { 160, 320, 600 })
        {
            var ratio = Math.Min(1d, Math.Min(width / (double)oriented.Width, width * 1.5 / oriented.Height));
            var outputInfo = new SKImageInfo(Math.Max(1, (int)Math.Round(oriented.Width * ratio)),
                Math.Max(1, (int)Math.Round(oriented.Height * ratio)), SKColorType.Rgba8888, SKAlphaType.Premul);
            using var resized = new SKBitmap(outputInfo);
            // White matte makes transparent input consistent in JPEG and WebP, and removes metadata.
            using (var canvas = new SKCanvas(resized))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(oriented, new SKRect(0, 0, outputInfo.Width, outputInfo.Height),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }
            using var image = SKImage.FromBitmap(resized);
            foreach (var format in new[] { SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Webp })
            {
                byte[]? bytes = null;
                foreach (var quality in new[] { 88, 82, 76 })
                {
                    using var encoded = image.Encode(format, quality) ?? throw new InvalidDataException("Image encoding failed.");
                    if (encoded.Size <= (width == 600 ? DetailBudget : ThumbnailBudget))
                    {
                        bytes = encoded.ToArray();
                        break;
                    }
                }
                if (bytes is null)
                    throw new InvalidDataException("This image cannot meet the public image budget at acceptable quality. Try a simpler or smaller cover.");
                result.Add(new(width, format == SKEncodedImageFormat.Jpeg ? ".jpg" : ".webp", outputInfo.Width, outputInfo.Height, bytes));
            }
        }
        return result;
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        var w = source.Width; var h = source.Height;
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity
        };
        canvas.SetMatrix(matrix);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return result;
    }
}
