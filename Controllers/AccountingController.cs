using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using PaymentWall.Models;
using PaymentWall.Services;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
using PaymentWall.Attributes;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountingController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        public AccountingController(IConnectionService connectionService)
        {
            _connectionService = connectionService;
        }
        #region Transfer Money
        public class _transferRequest
        {
            [Required]
            public int recipientWalletId { get; set; }
            [Required]
            [BsonRepresentation(BsonType.Decimal128)]
            public decimal amount { get; set; }
        }

        public class _transferResponse
        {
            public string type { get; set; }
            public string message { get; set; }
        }
        [CheckUserLogin]
        [HttpPost("[action]")]
        public ActionResult<_transferResponse> TransferFunds([FromBody] _transferRequest transferData)
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");

            var userIdFromSession = HttpContext.Session.GetString("id");
            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return Ok(new _transferResponse { type = "error", message = "User not authenticated." });
            }

            var recipientId = transferData.recipientWalletId;
            var recipient = _walletCollection.AsQueryable().FirstOrDefault(u => u._id == recipientId);
            if (recipient == null)
            {
                return Ok(new _transferResponse { type = "error", message = "Recipient wallet not found." });
            }

            ObjectId userId = ObjectId.Parse(userIdFromSession);

            var sender = _walletCollection.AsQueryable().FirstOrDefault(w => w.userId == userId && w.currency == recipient.currency);
            if (sender == null)
            {
                return Ok(new _transferResponse { type = "error", message = "Sender wallet not found." });
            }

            if (sender.balance < transferData.amount)
            {
                return Ok(new _transferResponse { type = "error", message = "Insufficient balance." });
            }

            var senderUpdate = Builders<Wallet>.Update.Inc(w => w.balance, -transferData.amount);
            _walletCollection.UpdateOne(u => u._id == sender._id, senderUpdate);

            var recipientUpdate = Builders<Wallet>.Update.Inc(w => w.balance, transferData.amount);
            _walletCollection.UpdateOne(u => u._id == recipient._id, recipientUpdate);

            var senderAccounting = new Accounting
            {
                userId = sender.userId,
                amount = -transferData.amount,
                currency = sender.currency,
                walletId = sender._id
            };
            _accountingCollection.InsertOne(senderAccounting);

            var recipientAccounting = new Accounting
            {
                userId = recipient.userId,
                amount = transferData.amount,
                currency = recipient.currency,
                walletId = transferData.recipientWalletId
            };
            _accountingCollection.InsertOne(recipientAccounting);

            return Ok(new _transferResponse { type = "success", message = "Transfer completed successfully." });
        }
        #endregion

        #region User Financial Report
        public class _financialReportRequest
        {
            public int? pageSize { get; set; } = 10;
            public int? pageNumber { get; set; } = 1;
            public DateTime? startDate { get; set; }
            public DateTime? endDate { get; set; }
        }

        public class _financialReportResponse
        {
            public string type { get; set; }
            public string message { get; set; }
            public decimal totalIncome { get; set; }
            public decimal totalExpense { get; set; }
            public decimal netBalance { get; set; }
        }
        [HttpPost("[action]"), CheckUserLogin]
        public ActionResult<_financialReportResponse> GetUserFinancialReport([FromBody] _financialReportRequest request)
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");

            var userIdFromSession = HttpContext.Session.GetString("id");
            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return Ok(new _financialReportResponse { type = "error", message = "User not logged in." });
            }

            ObjectId userIdObj;
            try
            {
                userIdObj = ObjectId.Parse(userIdFromSession);
            }
            catch
            {
                return Ok(new _financialReportResponse { type = "error", message = "Invalid userId format in session." });
            }

            var userWallets = _walletCollection.AsQueryable().Where(wallet => wallet.userId == userIdObj).ToList();

            decimal totalIncome = 0;
            decimal totalExpense = 0;
            decimal netBalance = 0;

            var skip = (request.pageNumber.Value - 1) * request.pageSize.Value;

            foreach (var wallet in userWallets)
            {
                var query = _accountingCollection.AsQueryable().Where(a => a.walletId == wallet._id);

                if (request.startDate.HasValue)
                {
                    query = query.Where(a => a.date >= request.startDate.Value);
                }

                if (request.endDate.HasValue)
                {
                    query = query.Where(a => a.date <= request.endDate.Value);
                }

                var accountingForWallet = query.Skip(skip).Take(request.pageSize.Value).ToList();

                foreach (var entry in accountingForWallet)
                {
                    if (entry.amount > 0) totalIncome += entry.amount;
                    else totalExpense += entry.amount;
                }
                netBalance += wallet.balance;
            }

            return Ok(new _financialReportResponse
            {
                type = "success",
                message = "Financial report fetched successfully.",
                totalIncome = totalIncome,
                totalExpense = Math.Abs(totalExpense),
                netBalance = netBalance
            });
        }

        #endregion

    }
}