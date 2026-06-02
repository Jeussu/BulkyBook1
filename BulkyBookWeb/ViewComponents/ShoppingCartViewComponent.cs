using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BulkyBookWeb.ViewComponents
{
    public class ShoppingCartViewComponent : ViewComponent
    {
        private readonly IUnitOfWork _unitOfWork;
        public ShoppingCartViewComponent(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public Task<IViewComponentResult> InvokeAsync()
        {
            var claimsIdentity = User.Identity as ClaimsIdentity;
            var claim = claimsIdentity?.FindFirst(ClaimTypes.NameIdentifier);
            if (claim != null)
            {
                var cartQuantity = _unitOfWork.ShoppingCart
                    .GetAll(u => u.ApplicationUserId == claim.Value)
                    .Sum(cart => cart.Count);

                HttpContext.Session.SetInt32(SD.SessionCart, cartQuantity);
                return Task.FromResult<IViewComponentResult>(View(cartQuantity));
            }
            else
            {
                HttpContext.Session.Remove(SD.SessionCart);
                return Task.FromResult<IViewComponentResult>(View(0));
            }
        }
    }
}
