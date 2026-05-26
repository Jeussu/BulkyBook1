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

    public IActionResult Index(string? searchTerm, int? categoryId, int? coverTypeId, double? minPrice, double? maxPrice, int page = 1, int pageSize = 8)
    {
        pageSize = pageSize is 8 or 12 or 20 ? pageSize : 8;
        page = Math.Max(1, page);

        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.CoverType)
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

        var totalItems = query.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Min(page, totalPages);

        var productList = query
            .OrderBy(p => p.Title)
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

        ShoppingCart cartObj = new()
        {
            Count = 1,
            ProductId = productId,
            Product = product,
        };
        return View(cartObj);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public IActionResult Details(ShoppingCart shoppingCart)
    {
        var invalidCount = shoppingCart.Count is < 1 or > 1000;
        if (invalidCount)
        {
            ModelState.AddModelError(nameof(ShoppingCart.Count), "Please enter a value between 1 and 1000.");
        }

        var product = _unitOfWork.Product.GetFirstOrDefault(u => u.Id == shoppingCart.ProductId, includeProperties: "Category,CoverType");
        if (product == null)
        {
            return NotFound();
        }

        if (invalidCount)
        {
            shoppingCart.Count = Math.Clamp(shoppingCart.Count, 1, 1000);
            shoppingCart.Product = product;
            return View(shoppingCart);
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
