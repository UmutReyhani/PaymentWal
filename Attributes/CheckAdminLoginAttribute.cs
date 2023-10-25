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
        public int[] Roles { get; set; }
        public CheckAdminLoginAttribute(params int[] roles)
        {
            Roles = roles;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var userIdFromSession = context.HttpContext.Session.GetString("id");
            var login = context.HttpContext.Session.GetString("login");
            if (string.IsNullOrEmpty(userIdFromSession) | login != "admin")
            {
                context.Result = new OkObjectResult(new { type = "error", message = "unauthorized" });
                return;
            }

            if (!IsUserAuthorized(userIdFromSession, context))
            {
                context.Result = new OkObjectResult(new { type = "error", message = "You are not authorized" });
                return;
            }
        }

        private bool IsUserAuthorized(string userId, AuthorizationFilterContext context)
        {
            var connectionService = (IConnectionService)context.HttpContext.RequestServices.GetService(typeof(IConnectionService));
            var _adminCollection = connectionService.db().GetCollection<Admin>("Admin");
            var adminUser = _adminCollection.AsQueryable().FirstOrDefault(a => a._id == ObjectId.Parse(userId));

            if (adminUser != null)
            {
                foreach (var allowedRole in Roles)
                {
                    if (adminUser.role == allowedRole)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
