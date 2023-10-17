using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;


namespace PaymentWall.Attributes
{
    public class CheckUserLogin : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var userJson = context.HttpContext.Session.GetString("id");
            var login = context.HttpContext.Session.GetString("login");
            if (string.IsNullOrEmpty(userJson) | login != "user")
            {
                context.Result = new OkObjectResult(new { type = "error", message = "unauthorized" });
                return;
            }
        }
    }
}
