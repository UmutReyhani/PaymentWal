using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using PaymentWall.Models;
using PaymentWall.Services;
using MongoDB.Driver;


namespace PaymentWall.Attributes
{
    public class CheckAdminLoginAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var userIdFromSession = context.HttpContext.Session.GetString("id");
            if (string.IsNullOrEmpty(userIdFromSession))
            {
                context.Result = new OkObjectResult(new { type = "error", message = "unauthorized" });
                return;
            }

            if (!IsUserAdmin(userIdFromSession, context))
            {
                context.Result = new OkObjectResult(new { type = "error", message = "You are not authorized as an admin" });
                return;
            }
        }

        private bool IsUserAdmin(string userId, AuthorizationFilterContext context)
        {
            var connectionService = (IConnectionService)context.HttpContext.RequestServices.GetService(typeof(IConnectionService));
            var _adminCollection = connectionService.db().GetCollection<Admin>("Admin");
            var adminUser = _adminCollection.AsQueryable().FirstOrDefault(a => a._id == ObjectId.Parse(userId));
            return adminUser != null;
        }

    }
}
