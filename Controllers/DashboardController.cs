using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using PaymentWall.Models;
using PaymentWall.Services;
using System.Linq;
using PaymentWall.Attributes;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        public DashboardController(IConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        #region User Wallets

        public class _walletsResponse
        {
            public _walletDetails[] wallets { get; set; }
        }

        public class _walletDetails
        {
            public int walletId { get; set; }
            public decimal balance { get; set; }
            public string currency { get; set; }
        }

        [HttpPost("[action]"), CheckUserLogin]
        public ActionResult<_walletsResponse> GetUserWallets()
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");

            var userIdFromSession = HttpContext.Session.GetString("id");
            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return Ok(new { message = "User not logged in." });
            }

            ObjectId userIdObj;
            try
            {
                userIdObj = ObjectId.Parse(userIdFromSession);
            }
            catch
            {
                return Ok(new { message = "Invalid userId format in session." });
            }

            var userWallets = _walletCollection.AsQueryable().Where(w => w.userId == userIdObj).ToList();

            var walletsResponse = new _walletsResponse
            {
                wallets = userWallets.Select(w => new _walletDetails
                {
                    walletId = w._id,
                    balance = w.balance,
                    currency = w.currency
                }).ToArray()
            };

            return Ok(walletsResponse);
        }

        #endregion

        #region Wallet Details

        public class _walletDetailsResponse
        {
            public string type { get; set; }
            public string message { get; set; }
            public Wallet walletInfo { get; set; }
            public Limit userLimits { get; set; }
            public Accounting[] recentTransactions { get; set; }
        }

        [HttpPost("[action]"), CheckUserLogin]
        public ActionResult<_walletDetailsResponse> GetWalletDetails(int walletId)
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _limitCollection = _connectionService.db().GetCollection<Limit>("Limit");
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");

            // Oturumdan kullanıcı ID'sini al
            var userIdFromSession = HttpContext.Session.GetString("id");

            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return Ok(new { message = "Unauthorized request. User not found in session." });
            }

            var userObjectId = ObjectId.Parse(userIdFromSession);

            // Kullanıcıya ait tüm cüzdanları sorgula
            var userWallets = _walletCollection.AsQueryable().Where(w => w.userId == userObjectId).ToList();

            if (!userWallets.Any(w => w._id == walletId))
            {
                return Ok(new { message = "Unauthorized request. This wallet does not belong to the current user." });
            }

            var userLimits = _limitCollection.AsQueryable().FirstOrDefault();

            // Son 5 işlemi al
            var recentTransactions = _accountingCollection.AsQueryable()
                .Where(a => a.walletId == walletId)
                .OrderByDescending(a => a.date)
                .Take(5)
                .ToList();
            var walletDetails = new _walletDetailsResponse
            {
                type = "success",
                message = "Details fetched successfully.",
                walletInfo = userWallets.First(w => w._id == walletId),  // Şu anki cüzdanın bilgisi
                userLimits = userLimits,
                recentTransactions = recentTransactions.ToArray()
            };

            return Ok(walletDetails);
        }

        #endregion
    }
}
