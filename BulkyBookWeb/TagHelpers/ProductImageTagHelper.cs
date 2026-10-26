using BulkyBookWeb.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BulkyBookWeb.TagHelpers;

[HtmlTargetElement("product-image")]
public sealed class ProductImageTagHelper(ProductImageStore store) : TagHelper
{
    public string? ImageUrl { get; set; }
    public string Variant { get; set; } = "thumbnail";
    public string Alt { get; set; } = "";
    public string? Class { get; set; }
    public string Loading { get; set; } = "lazy";
    public string Sizes { get; set; } = "144px";
    public bool HideOnMobile { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "picture";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.Clear();
        output.Attributes.SetAttribute("class", "product-picture");
        if (HideOnMobile)
        {
            var mobile = new Microsoft.AspNetCore.Mvc.Rendering.TagBuilder("source");
            mobile.TagRenderMode = Microsoft.AspNetCore.Mvc.Rendering.TagRenderMode.SelfClosing;
            mobile.Attributes["media"] = "(max-width: 767px)";
            mobile.Attributes["srcset"] = ProductImageStore.Placeholder;
            output.Content.AppendHtml(mobile);
        }
        var images = store.Resolve(ImageUrl);
        var img = new Microsoft.AspNetCore.Mvc.Rendering.TagBuilder("img");
        img.TagRenderMode = Microsoft.AspNetCore.Mvc.Rendering.TagRenderMode.SelfClosing;
        img.Attributes["alt"] = Alt;
        img.Attributes["loading"] = Loading == "eager" ? "eager" : "lazy";
        img.Attributes["decoding"] = "async";
        img.Attributes["data-image-fallback"] = ProductImageStore.Placeholder;
        if (!string.IsNullOrWhiteSpace(Class)) img.Attributes["class"] = Class;
        if (images is null)
            img.Attributes["src"] = store.Fallback(ImageUrl);
        else
        {
            var lowJpeg = Variant == "detail" ? images.ThumbnailJpeg : images.SmallJpeg;
            var highJpeg = Variant == "detail" ? images.DetailJpeg : images.ThumbnailJpeg;
            var lowWebp = Variant == "detail" ? images.ThumbnailWebp : images.SmallWebp;
            var highWebp = Variant == "detail" ? images.DetailWebp : images.ThumbnailWebp;
            var source = new Microsoft.AspNetCore.Mvc.Rendering.TagBuilder("source");
            source.TagRenderMode = Microsoft.AspNetCore.Mvc.Rendering.TagRenderMode.SelfClosing;
            source.Attributes["type"] = "image/webp";
            source.Attributes["srcset"] = lowWebp.Width == highWebp.Width ? highWebp.Url : $"{lowWebp.Url} {lowWebp.Width}w, {highWebp.Url} {highWebp.Width}w";
            source.Attributes["sizes"] = Sizes;
            img.Attributes["src"] = highJpeg.Url;
            img.Attributes["srcset"] = lowJpeg.Width == highJpeg.Width ? highJpeg.Url : $"{lowJpeg.Url} {lowJpeg.Width}w, {highJpeg.Url} {highJpeg.Width}w";
            img.Attributes["sizes"] = Sizes;
            img.Attributes["width"] = highJpeg.Width.ToString();
            img.Attributes["height"] = highJpeg.Height.ToString();
            output.Content.AppendHtml(source);
        }
        output.Content.AppendHtml(img);
    }
}
