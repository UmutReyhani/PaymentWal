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
            public simpleWalletDetails walletInfo { get; set; }
            public simpleAccountingDetails[] recentTransactions { get; set; }
        }

        public class simpleWalletDetails
        {
            public int _id { get; set; }
            public decimal balance { get; set; }
            public string currency { get; set; }
            public int status { get; set; }
        }

        public class simpleAccountingDetails
        {
            public decimal amount { get; set; }
            public int walletId { get; set; }
            public string currency { get; set; }
            public DateTimeOffset date { get; set; }
            public string senderName { get; set; }  
            public string recipientName { get; set; }
        }

        [HttpPost("[action]"), CheckUserLogin]
        public ActionResult<_walletDetailsResponse> GetWalletDetails(_walletDetailsRequest request)
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
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

            var recentTransactions = _accountingCollection.AsQueryable()
                .Where(a => a.walletId == request.walletId)
                .OrderByDescending(a => a.date)
                .Take(5)
                .ToList();

            var transactionDetails = recentTransactions.Select(a => {
                var sender = _userCollection.AsQueryable().FirstOrDefault(u => u._id == a.senderUserId);
                var recipient = _userCollection.AsQueryable().FirstOrDefault(u => u._id == a.recipientUserId);

                return new simpleAccountingDetails
                {
                    amount = a.amount,
                    walletId = a.walletId,
                    currency = a.currency,
                    date = a.date,
                    senderName = sender?.name,
                    recipientName = recipient?.name
                };
            }).ToArray();

            var walletDetails = new _walletDetailsResponse
            {
                type = "success",
                message = "Details fetched successfully.",
                walletInfo = new simpleWalletDetails
                {
                    _id = userWallets.First(w => w._id == request.walletId)._id,
                    balance = userWallets.First(w => w._id == request.walletId).balance,
                    currency = userWallets.First(w => w._id == request.walletId).currency,
                    status = userWallets.First(w => w._id == request.walletId).status
                },
                recentTransactions = transactionDetails
            };

            return Ok(walletDetails);
        }

        #endregion

    }
}
