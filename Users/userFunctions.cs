using Microsoft.AspNetCore.Http;
using PaymentWall.Models;
using System.Text.Json;

namespace MuhasebeFull.Users
{
    public class userFunctions
    {
        #region UserSession
        public static void SetCurrentUserToSession(HttpContext context, PaymentWall.Models.Users user)
        {
            context.Session.SetString("id", user._id.ToString());
        }
        #endregion
    }
}