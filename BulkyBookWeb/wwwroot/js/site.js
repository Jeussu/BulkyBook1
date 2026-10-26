// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// A failed WebP/source selection must also remove the picture source before using the fallback.
function useProductImageFallback(image) {
    if (!(image instanceof HTMLImageElement) || !image.dataset.imageFallback) return;
    const fallback = image.dataset.imageFallback;
    delete image.dataset.imageFallback;
    if (image.parentElement?.tagName === "PICTURE") {
        image.parentElement.querySelectorAll("source").forEach(source => source.remove());
    }
    image.removeAttribute("srcset");
    image.removeAttribute("sizes");
    image.src = fallback;
}
document.addEventListener("error", function (event) {
    useProductImageFallback(event.target);
}, true);
// An eager image can fail before this footer script registers its error listener.
document.querySelectorAll("img[data-image-fallback]").forEach(function (image) {
    if (image.complete && image.naturalWidth === 0) useProductImageFallback(image);
});
