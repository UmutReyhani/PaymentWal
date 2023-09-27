using PaymentWall.Models;

namespace PaymentWall.User
{
    public class userFunctions
    {
        #region UserSession
        public static void SetCurrentUserToSession(HttpContext context, Users user)
        {
            context.Session.SetString("id", user._id.ToString());
        }
        public static void ClearCurrentUserFromSession(HttpContext context)
        {
            context.Session.Remove("id");
        }
        #endregion
    }
}