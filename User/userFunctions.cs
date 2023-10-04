using PaymentWall.Models;

namespace PaymentWall.User
{
    public class userFunctions
    {
        #region UserSession get id 
        public static void SetCurrentUserToSession(HttpContext context, Users user)
        {
            context.Session.SetString("id", user._id.ToString());
        }
        #endregion

        #region User session end
        public static void ClearCurrentUserFromSession(HttpContext context)
        {
            context.Session.Remove("id");
        }
        #endregion
    }
}