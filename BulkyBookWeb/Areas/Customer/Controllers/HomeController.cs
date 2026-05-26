using BulkyBook.DataAccess;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Claims;

namespace BulkyBookWeb.Controllers;

[Area("Customer")]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ApplicationDbContext _db;

    public HomeController(ILogger<HomeController> logger, IUnitOfWork unitOfWork, ApplicationDbContext db)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _db = db;
    }

    public IActionResult Index(string? searchTerm, int? categoryId, int? coverTypeId, double? minPrice, double? maxPrice, string? sortBy, int page = 1, int pageSize = 8)
    {
        pageSize = pageSize is 8 or 12 or 20 ? pageSize : 8;
        page = Math.Max(1, page);

        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.CoverType)
            .Where(p => p.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Title, $"%{term}%") ||
                EF.Functions.Like(p.Author, $"%{term}%") ||
                EF.Functions.Like(p.ISBN, $"%{term}%"));
        }

        if (categoryId.GetValueOrDefault() > 0)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (coverTypeId.GetValueOrDefault() > 0)
        {
            query = query.Where(p => p.CoverTypeId == coverTypeId);
        }

        if (minPrice.HasValue)
        {
            query = query.Where(p => p.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p => p.Price <= maxPrice.Value);
        }

        query = sortBy switch
        {
            "title_asc" => query.OrderBy(p => p.Title),
            "title_desc" => query.OrderByDescending(p => p.Title),
            "price_asc" => query.OrderBy(p => p.Price),
            "price_desc" => query.OrderByDescending(p => p.Price),
            _ => query.OrderByDescending(p => p.Id)
        };

        var totalItems = query.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Min(page, totalPages);

        var productList = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var model = new ProductListVM
        {
            Products = productList,
            CategoryList = _db.Categories.AsNoTracking().OrderBy(c => c.DisplayOrder).Select(c => new SelectListItem
            {
                Text = c.Name,
                Value = c.Id.ToString()
            }).ToList(),
            CoverTypeList = _db.CoverTypes.AsNoTracking().OrderBy(c => c.Name).Select(c => new SelectListItem
            {
                Text = c.Name,
                Value = c.Id.ToString()
            }).ToList(),
            SearchTerm = searchTerm,
            CategoryId = categoryId,
            CoverTypeId = coverTypeId,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            SortBy = sortBy,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };

        return View(model);
    }

    public IActionResult Details(int productId)
    {
        var product = _unitOfWork.Product.GetFirstOrDefault(u => u.Id == productId, includeProperties: "Category,CoverType");
        if (product == null)
        {
            return NotFound();
        }

        return View(BuildProductDetailsVm(product));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public IActionResult Details(ProductDetailsVM model)
    {
        var shoppingCart = model.ShoppingCart;
        var invalidCount = shoppingCart.Count is < 1 or > 1000;
        if (invalidCount)
        {
            ModelState.AddModelError("ShoppingCart.Count", "Please enter a value between 1 and 1000.");
        }

        var product = _unitOfWork.Product.GetFirstOrDefault(u => u.Id == shoppingCart.ProductId, includeProperties: "Category,CoverType");
        if (product == null)
        {
            return NotFound();
        }

        var unavailable = false;
        if (!product.IsActive)
        {
            unavailable = true;
            ModelState.AddModelError("ShoppingCart.ProductId", "This book is not currently available.");
        }

        if (product.StockQuantity <= 0)
        {
            unavailable = true;
            ModelState.AddModelError("ShoppingCart.Count", "This book is currently out of stock.");
        }

        if (invalidCount || unavailable)
        {
            shoppingCart.Count = Math.Clamp(shoppingCart.Count, 1, 1000);
            return View(BuildProductDetailsVm(product, shoppingCart.Count));
        }

        var claimsIdentity = (ClaimsIdentity)User.Identity!;
        var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null)
        {
            return Challenge();
        }

        shoppingCart.ApplicationUserId = claim.Value;

        ShoppingCart cartFromDb = _unitOfWork.ShoppingCart.GetFirstOrDefault(
            u => u.ApplicationUserId == claim.Value && u.ProductId == shoppingCart.ProductId);

        var requestedQuantity = shoppingCart.Count + (cartFromDb?.Count ?? 0);
        if (requestedQuantity > product.StockQuantity)
        {
            ModelState.AddModelError("ShoppingCart.Count", $"Only {product.StockQuantity} copies are available.");
            return View(BuildProductDetailsVm(product, shoppingCart.Count));
        }

        if (cartFromDb == null)
        {
            _unitOfWork.ShoppingCart.Add(shoppingCart);
            _unitOfWork.Save();
            HttpContext.Session.SetInt32(SD.SessionCart,
                _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == claim.Value).ToList().Count);
        }
        else
        {
            _unitOfWork.ShoppingCart.IncrementCount(cartFromDb, shoppingCart.Count);
            _unitOfWork.Save();
        }

        return RedirectToAction(nameof(Index));
    }

    private ProductDetailsVM BuildProductDetailsVm(Product product, int count = 1)
    {
        return new ProductDetailsVM
        {
            ShoppingCart = new ShoppingCart
            {
                Count = count,
                ProductId = product.Id,
                Product = product
            },
            RelatedProducts = _db.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.CoverType)
                .Where(p => p.IsActive && p.CategoryId == product.CategoryId && p.Id != product.Id)
                .OrderBy(p => p.Title)
                .Take(4)
                .ToList()
        };
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
