using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using PaymentWall.Models;
using PaymentWall.Services;
using System.Linq;

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
            public _walletDetails[] Wallets { get; set; }
        }

        public class _walletDetails
        {
            public int WalletId { get; set; }
            public decimal Balance { get; set; }
            public string Currency { get; set; }
        }

        [HttpPost("getUserWallets")]
        public ActionResult<_walletsResponse> GetUserWallets()
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");

            // Oturumdan kullanıcı ID'sini al
            var userIdFromSession = HttpContext.Session.GetString("id");
            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return BadRequest(new { message = "User not logged in." });
            }

            ObjectId userIdObj;
            try
            {
                userIdObj = ObjectId.Parse(userIdFromSession);
            }
            catch
            {
                return BadRequest(new { message = "Invalid userId format in session." });
            }

            var userWallets = _walletCollection.Find(w => w.userId == userIdObj).ToList();

            var walletsResponse = new _walletsResponse
            {
                Wallets = userWallets.Select(w => new _walletDetails
                {
                    WalletId = w._id,
                    Balance = w.balance,
                    Currency = w.currency
                }).ToArray()
            };

            return Ok(walletsResponse);
        }

        #endregion

        #region Wallet Details

        public class _walletDetailsResponse
        {
            public string Type { get; set; }
            public string Message { get; set; }
            public Wallet WalletInfo { get; set; }
            public Limit UserLimits { get; set; }
            public Accounting[] RecentTransactions { get; set; }
        }

        [HttpPost("getWalletDetails")]
        public ActionResult<_walletDetailsResponse> GetWalletDetails(int walletId)
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _limitCollection = _connectionService.db().GetCollection<Limit>("Limit");
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");

            // Oturumdan kullanıcı ID'sini al
            var userIdFromSession = HttpContext.Session.GetString("id");

            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return BadRequest(new { message = "Unauthorized request. User not found in session." });
            }

            var userObjectId = ObjectId.Parse(userIdFromSession);

            // Kullanıcıya ait tüm cüzdanları sorgula
            var userWallets = _walletCollection.Find(w => w.userId == userObjectId).ToList();

            if (!userWallets.Any(w => w._id == walletId))
            {
                return BadRequest(new { message = "Unauthorized request. This wallet does not belong to the current user." });
            }

            var userLimits = _limitCollection.Find(Builders<Limit>.Filter.Empty).FirstOrDefault();

            // Son 5 işlemi al
            var recentTransactions = _accountingCollection.Find(a => a.walletId == walletId).SortByDescending(a => a.date).Limit(5).ToList();

            var walletDetails = new _walletDetailsResponse
            {
                Type = "success",
                Message = "Details fetched successfully.",
                WalletInfo = userWallets.First(w => w._id == walletId),  // Şu anki cüzdanın bilgisi
                UserLimits = userLimits,
                RecentTransactions = recentTransactions.ToArray()
            };

            return Ok(walletDetails);
        }

        #endregion
    }
}
