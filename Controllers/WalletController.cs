using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using PaymentWall.Models;
using PaymentWall.Services;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using PaymentWall.Attributes;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WalletController : ControllerBase
    {
        private readonly IConnectionService _connectionService;

        public WalletController(IConnectionService connectionService)
        {
            _connectionService = connectionService;
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
                return Ok(new _listWalletRes { type = "error", message = "User not logged in." });
            }

            ObjectId userIdObj;
            try
            {
                userIdObj = ObjectId.Parse(userIdFromSession);
            }
            catch
            {
                return Ok(new _listWalletRes { type = "error", message = "Invalid userId format in session." });
            }

            var userWallets = _walletCollection.AsQueryable().Where(wallet => wallet.userId == userIdObj).ToList();

            if (userWallets == null || userWallets.Count == 0)
            {
                return Ok(new _listWalletRes { type = "error", message = "No wallets found for this userId." });
            }

            return Ok(new _listWalletRes { type = "success", message = "Wallets fetched successfully.", wallets = userWallets });
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
                return Ok(new _createWalletRes { type = "error", message = "Unauthorized request. User not found in session." });
            }

            var user = _userCollection.AsQueryable().FirstOrDefault(u => u._id.ToString() == userIdFromSession);
            if (user == null)
            {
                return Ok(new _createWalletRes { type = "error", message = "User not found." });
            }

            var existingWallet = _walletCollection.AsQueryable().FirstOrDefault(w => w.userId.ToString() == userIdFromSession && w.currency == req.currency);
            if (existingWallet != null)
            {
                return Ok(new _createWalletRes { type = "error", message = "Wallet with this currency already exists for the user." });
            }

            Wallet newWallet = new Wallet
            {
                _id = GenerateUniqueWalletId(),
                userId = ObjectId.Parse(userIdFromSession),
                balance = 0,
                currency = req.currency
            };

            _walletCollection.InsertOne(newWallet);

            return Ok(new _createWalletRes { type = "success", message = "Wallet created successfully." });
        }

        #endregion

    }
}
