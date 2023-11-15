using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using PaymentWall.Models;
using PaymentWall.Services;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using PaymentWall.Attributes;
using Microsoft.Extensions.Localization;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WalletController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        private readonly IStringLocalizer _localizer;


        public WalletController(IConnectionService connectionService, IStringLocalizer<UserController> localizer)
        {
            _connectionService = connectionService;
            _localizer = localizer;
        }
        #region Generateuniqwalletid
        private int GenerateUniqueWalletId()
        {
            Random random = new Random();
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            int walletId;
            bool isUnique = false;

            do
            {
                walletId = random.Next(10000000, 99999999);
                var existingWallet = _walletCollection.AsQueryable().FirstOrDefault(w => w._id == walletId);
                if (existingWallet == null)
                {
                    isUnique = true;
                }
            } while (!isUnique);

            return walletId;
        }
        #endregion

        #region List Wallet

        public class _listWalletRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
            public List<Wallet> wallets { get; set; }
        }

        [HttpGet("[action]"), CheckUserLogin]
        public ActionResult<_listWalletRes> ListWalletsByUserId()
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");

            var userIdFromSession = HttpContext.Session.GetString("id");
            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return Ok(new _listWalletRes { type = "error", message = _localizer["userNotLoggedIn"].Value });
            }

            ObjectId userIdObj;
            try
            {
                userIdObj = ObjectId.Parse(userIdFromSession);
            }
            catch
            {
                return Ok(new _listWalletRes { type = "error", message = _localizer["invalidUserIdFormat"].Value });
            }

            var userWallets = _walletCollection.AsQueryable().Where(wallet => wallet.userId == userIdObj).ToList();

            if (userWallets == null || userWallets.Count == 0)
            {
                return Ok(new _listWalletRes { type = "error", message = _localizer["noWalletsForUserId"].Value });
            }

            return Ok(new _listWalletRes { type = "success", message = _localizer["walletsFetchedSuccessfully"].Value, wallets = userWallets });
        }

        #endregion

        #region Create Wallet

        public class _createWalletReq
        {
            [Required]
            public string currency { get; set; }
        }

        public class _createWalletRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]"), CheckUserLogin]
        public ActionResult<_createWalletRes> CreateWallet([FromBody] _createWalletReq req)
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");

            var userIdFromSession = HttpContext.Session.GetString("id");

            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return Ok(new _createWalletRes { type = "error", message = _localizer["unauthorizedRequest"].Value });
            }

            var user = _userCollection.AsQueryable().FirstOrDefault(u => u._id.ToString() == userIdFromSession);
            if (user == null)
            {
                return Ok(new _createWalletRes { type = "error", message = _localizer["43"].Value });
            }

            var existingWallet = _walletCollection.AsQueryable().FirstOrDefault(w => w.userId.ToString() == userIdFromSession && w.currency == req.currency);
            if (existingWallet != null)
            {
                return Ok(new _createWalletRes { type = "error", message = _localizer["walletAlreadyExists"].Value });
            }

            Wallet newWallet = new Wallet
            {
                _id = GenerateUniqueWalletId(),
                userId = ObjectId.Parse(userIdFromSession),
                balance = 0,
                currency = req.currency,
                status = 1,
            };

            _walletCollection.InsertOne(newWallet);

            return Ok(new _createWalletRes { type = "success", message = _localizer["walletCreatedSuccessfully"].Value });
        }

        #endregion

    }
}
