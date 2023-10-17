using PaymentWall.Models;

namespace PaymentWall.User
{
    public class userFunctions
    {
        #region UserSession get id 
        public static void SetCurrentUserToSession(HttpContext context, Users user)
        {
            context.Session.SetString("id", user._id.ToString());
            context.Session.SetString("login", "user");
        }
        #endregion

        #region User session end
        public static void ClearCurrentUserFromSession(HttpContext context)
        {
            context.Session.Remove("id");
            context.Session.Clear();
        }
        #endregion

        public static void SetCurrentAdminToSession(HttpContext context, Admin user)
        {
            context.Session.SetString("id", user._id.ToString());
            context.Session.SetString("login", "admin");
        }
        public static void ClearCurrentAdminFromSession(HttpContext context)
        {
            context.Session.Remove("id");
            context.Session.Clear();
        }
    }
}