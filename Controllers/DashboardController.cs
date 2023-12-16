using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using PaymentWall.Models;
using PaymentWall.Services;
using System.Linq;
using PaymentWall.Attributes;
using Microsoft.Extensions.Localization;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        private readonly IStringLocalizer _localizer;

        public DashboardController(IConnectionService connectionService, IStringLocalizer<UserController> localizer)
        {
            _connectionService = connectionService;
            _localizer = localizer;
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
                return Ok(new { message = _localizer["53"].Value });
            }

            ObjectId userIdObj;
            try
            {
                userIdObj = ObjectId.Parse(userIdFromSession);
            }
            catch
            {
                return Ok(new { message = _localizer["54"].Value });
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

        public class _walletDetailsRequest
        {
            public int walletId { get; set; }
        }

        public class _walletDetailsResponse
        {
            public string type { get; set; }
            public string message { get; set; }
            public Wallet walletInfo { get; set; }
            public Limit userLimits { get; set; }
            public Accounting[] recentTransactions { get; set; }
        }

        [HttpPost("[action]"), CheckUserLogin]
        public ActionResult<_walletDetailsResponse> GetWalletDetails(_walletDetailsRequest request)
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _limitCollection = _connectionService.db().GetCollection<Limit>("Limit");
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");

            var userIdFromSession = HttpContext.Session.GetString("id");

            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return Ok(new { message = _localizer["55"].Value });
            }

            var userObjectId = ObjectId.Parse(userIdFromSession);

            var userWallets = _walletCollection.AsQueryable().Where(w => w.userId == userObjectId).ToList();

            if (!userWallets.Any(w => w._id == request.walletId))
            {
                return Ok(new { message = _localizer["56"].Value });
            }

            var userLimits = _limitCollection.AsQueryable().FirstOrDefault();

            var recentTransactions = _accountingCollection.AsQueryable()
                .Where(a => a.walletId == request.walletId)
                .OrderByDescending(a => a.date)
                .Take(5)
                .ToList();
            var walletDetails = new _walletDetailsResponse
            {
                type = "success",
                message = "Details fetched successfully.",
                walletInfo = userWallets.First(w => w._id == request.walletId),
                userLimits = userLimits,
                recentTransactions = recentTransactions.ToArray()
            };

            return Ok(walletDetails);
        }

        #endregion
    }
}
