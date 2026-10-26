using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using BulkyBookWeb.Services;

namespace BulkyBookWeb.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class ProductController : Controller
{
    private const long MaxImageBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif"
    };

    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProductController> _logger;
    private readonly ProductImageStore _imageStore;

    public ProductController(IUnitOfWork unitOfWork, ILogger<ProductController> logger, ProductImageStore imageStore)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _imageStore = imageStore;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Upsert(int? id)
    {
        ProductVM productVM = CreateProductVm();

        if (id == null || id == 0)
        {
            return View(productVM);
        }

        var product = _unitOfWork.Product.GetFirstOrDefault(u => u.Id == id);
        if (product == null)
        {
            return NotFound();
        }

        productVM.Product = product;
        return View(productVM);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> Upsert(ProductVM obj, IFormFile? file)
    {
        var existingProduct = obj.Product.Id == 0
            ? null
            : _unitOfWork.Product.GetFirstOrDefault(u => u.Id == obj.Product.Id, tracked: false);

        if (obj.Product.Id != 0 && existingProduct == null)
        {
            return NotFound();
        }

        if (obj.Product.Id == 0 && file == null)
        {
            ModelState.AddModelError("file", "Product image is required.");
        }

        if (file != null && !IsValidProductImage(file, out var uploadError))
        {
            _logger.LogWarning("Rejected product image upload. FileName={FileName}, ContentType={ContentType}, Length={Length}, Reason={Reason}",
                file.FileName, file.ContentType, file.Length, uploadError);
            ModelState.AddModelError("file", uploadError);
        }

        if (!ModelState.IsValid)
        {
            obj.CategoryList = GetCategoryList();
            obj.CoverTypeList = GetCoverTypeList();
            return View(obj);
        }

        obj.Product.Description = SafeHtml.Sanitize(obj.Product.Description);

        if (file != null)
        {
            try
            {
                obj.Product.ImageUrl = await _imageStore.SaveAsync(file, HttpContext.RequestAborted);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Product image processing failed.");
                ModelState.AddModelError("file", exception is InvalidDataException ? exception.Message : "The image could not be saved. Please try again.");
                obj.CategoryList = GetCategoryList();
                obj.CoverTypeList = GetCoverTypeList();
                return View(obj);
            }
        }
        else if (!string.IsNullOrWhiteSpace(existingProduct?.ImageUrl))
        {
            obj.Product.ImageUrl = existingProduct.ImageUrl;
        }

        var creating = obj.Product.Id == 0;
        try
        {
            if (creating) _unitOfWork.Product.Add(obj.Product);
            else _unitOfWork.Product.Update(obj.Product);
            _unitOfWork.Save();
        }
        catch
        {
            if (file != null) _imageStore.DeleteOwned(obj.Product.ImageUrl);
            throw;
        }
        if (file != null) TryDeleteProductImage(existingProduct?.ImageUrl);
        TempData["success"] = creating ? "Product created successfully" : "Product updated successfully";
        return RedirectToAction("Index");
    }

    #region API CALLS
    [HttpGet]
    public IActionResult GetAll()
    {
        var productList = _unitOfWork.Product.GetAll(includeProperties: "Category,CoverType");
        return Json(new { data = productList });
    }

    [HttpDelete]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int? id)
    {
        var obj = _unitOfWork.Product.GetFirstOrDefault(u => u.Id == id);
        if (obj == null)
        {
            return Json(new { success = false, message = "Error while deleting" });
        }

        var hasOrderHistory = _unitOfWork.OrderDetail.GetFirstOrDefault(u => u.ProductId == obj.Id, tracked: false) != null;
        if (hasOrderHistory)
        {
            return Json(new { success = false, message = "Product cannot be deleted because it appears in order history. Mark it inactive instead." });
        }

        var hasCartRows = _unitOfWork.ShoppingCart.GetFirstOrDefault(u => u.ProductId == obj.Id, tracked: false) != null;
        if (hasCartRows)
        {
            return Json(new { success = false, message = "Product cannot be deleted because it is currently in a shopping cart. Mark it inactive instead." });
        }

        _unitOfWork.Product.Remove(obj);
        _unitOfWork.Save();
        TryDeleteProductImage(obj.ImageUrl);
        return Json(new { success = true, message = "Delete Successful" });
    }
    #endregion

    private ProductVM CreateProductVm()
    {
        return new ProductVM
        {
            Product = new Product(),
            CategoryList = GetCategoryList(),
            CoverTypeList = GetCoverTypeList()
        };
    }

    private IEnumerable<SelectListItem> GetCategoryList()
    {
        return _unitOfWork.Category.GetAll().Select(i => new SelectListItem
        {
            Text = i.Name,
            Value = i.Id.ToString()
        });
    }

    private IEnumerable<SelectListItem> GetCoverTypeList()
    {
        return _unitOfWork.CoverType.GetAll().Select(i => new SelectListItem
        {
            Text = i.Name,
            Value = i.Id.ToString()
        });
    }

    private static bool IsValidProductImage(IFormFile file, out string error)
    {
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedImageExtensions.Contains(extension))
        {
            error = "Only .jpg, .jpeg, .png, .webp, and .gif images are allowed.";
            return false;
        }

        if (!AllowedImageContentTypes.Contains(file.ContentType))
        {
            error = "The uploaded file content type is not an allowed image type.";
            return false;
        }

        if (file.Length <= 0 || file.Length > MaxImageBytes)
        {
            error = "Product image must be between 1 byte and 2 MB.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void TryDeleteProductImage(string? imageUrl) => _imageStore.DeleteOwned(imageUrl);
}
