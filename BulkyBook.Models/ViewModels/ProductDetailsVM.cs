using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace BulkyBook.Models.ViewModels;

public class ProductDetailsVM
{
    public ShoppingCart ShoppingCart { get; set; } = new();

    [ValidateNever]
    public IEnumerable<Product> RelatedProducts { get; set; } = Enumerable.Empty<Product>();
}
