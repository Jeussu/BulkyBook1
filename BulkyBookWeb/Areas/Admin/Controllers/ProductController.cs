using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

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
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly ILogger<ProductController> _logger;

    public ProductController(IUnitOfWork unitOfWork, IWebHostEnvironment hostEnvironment, ILogger<ProductController> logger)
    {
        _unitOfWork = unitOfWork;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
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
    public IActionResult Upsert(ProductVM obj, IFormFile? file)
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
            var uploadDirectory = Path.Combine(_hostEnvironment.WebRootPath, "images", "products");
            Directory.CreateDirectory(uploadDirectory);

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid()}{extension}";
            var targetPath = Path.Combine(uploadDirectory, fileName);

            using (var fileStream = new FileStream(targetPath, FileMode.CreateNew))
            {
                file.CopyTo(fileStream);
            }

            if (!string.IsNullOrWhiteSpace(existingProduct?.ImageUrl))
            {
                TryDeleteProductImage(existingProduct.ImageUrl);
            }

            obj.Product.ImageUrl = $@"\images\products\{fileName}";
        }
        else if (!string.IsNullOrWhiteSpace(existingProduct?.ImageUrl))
        {
            obj.Product.ImageUrl = existingProduct.ImageUrl;
        }

        if (obj.Product.Id == 0)
        {
            _unitOfWork.Product.Add(obj.Product);
            TempData["success"] = "Product created successfully";
        }
        else
        {
            _unitOfWork.Product.Update(obj.Product);
            TempData["success"] = "Product updated successfully";
        }

        _unitOfWork.Save();
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

        TryDeleteProductImage(obj.ImageUrl);

        _unitOfWork.Product.Remove(obj);
        _unitOfWork.Save();
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

    private void TryDeleteProductImage(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        var uploadDirectory = Path.GetFullPath(Path.Combine(_hostEnvironment.WebRootPath, "images", "products")) + Path.DirectorySeparatorChar;
        var relativePath = imageUrl.TrimStart('\\', '/').Replace('/', Path.DirectorySeparatorChar);
        if (relativePath.StartsWith(Path.Combine("images", "products", "book-covers"), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var imagePath = Path.GetFullPath(Path.Combine(_hostEnvironment.WebRootPath, relativePath));

        if (!imagePath.StartsWith(uploadDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Skipped product image delete outside upload directory. ImageUrl={ImageUrl}", imageUrl);
            return;
        }

        if (System.IO.File.Exists(imagePath))
        {
            System.IO.File.Delete(imagePath);
        }
    }
}
