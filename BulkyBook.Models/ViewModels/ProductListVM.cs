using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BulkyBook.Models.ViewModels
{
    public class ProductListVM
    {
        public IEnumerable<Product> Products { get; set; } = new List<Product>();

        [ValidateNever]
        public IEnumerable<SelectListItem> CategoryList { get; set; } = new List<SelectListItem>();

        [ValidateNever]
        public IEnumerable<SelectListItem> CoverTypeList { get; set; } = new List<SelectListItem>();

        public string? SearchTerm { get; set; }
        public int? CategoryId { get; set; }
        public int? CoverTypeId { get; set; }
        public double? MinPrice { get; set; }
        public double? MaxPrice { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 8;
        public int TotalItems { get; set; }

        public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }
}
