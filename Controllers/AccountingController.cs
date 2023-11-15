using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using PaymentWall.Models;
using PaymentWall.Services;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
using PaymentWall.Attributes;
using Microsoft.Extensions.Localization;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountingController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        private readonly IStringLocalizer _localizer;

        public AccountingController(IConnectionService connectionService, IStringLocalizer<UserController> localizer)
        {
            _connectionService = connectionService;
            _localizer = localizer;
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
                return Ok(new _transferResponse { type = "error", message = _localizer["1"].Value });
            }

            var recipientId = transferData.recipientWalletId;
            var recipient = _walletCollection.AsQueryable().FirstOrDefault(u => u._id == recipientId);
            if (recipient == null)
            {
                return Ok(new _transferResponse { type = "error", message = _localizer["2"].Value });
            }

            ObjectId userId = ObjectId.Parse(userIdFromSession);

            var sender = _walletCollection.AsQueryable().FirstOrDefault(w => w.userId == userId && w.currency == recipient.currency);
            if (sender == null)
            {
                return Ok(new _transferResponse { type = "error", message = _localizer["3"].Value });
            }

            if (sender.balance < transferData.amount)
            {
                return Ok(new _transferResponse { type = "error", message = _localizer["4"].Value });
            }

            //limitler
            var _limitCollection = _connectionService.db().GetCollection<Limit>("Limit");
            var currentLimit = _limitCollection.AsQueryable().FirstOrDefault();

            if (currentLimit != null)
            {
                if (transferData.amount > currentLimit.maxTransfer)
                {
                    return Ok(new _transferResponse { type = "error", message = _localizer["limitExceededMaxTransfer"].Value });
                }

                if (transferData.amount < currentLimit.minTransfer)
                {
                    return Ok(new _transferResponse { type = "error", message = _localizer["limitErrorMinTransfer"].Value });
                }

                // Günlük transfer kontrolü
                var dailyTransfers = _accountingCollection.AsQueryable().Where(a => a.userId == userId && a.date > DateTime.UtcNow.AddDays(-1)).Sum(a => a.amount);
                if ((dailyTransfers + transferData.amount) > currentLimit.dailyMaxTransfer)
                {
                    return Ok(new _transferResponse { type = "error", message = _localizer["limitExceededDailyMaxTransfer"].Value });
                }

                // Aylık transfer kontrolü
                var monthlyTransfers = _accountingCollection.AsQueryable().Where(a => a.userId == userId && a.date > DateTime.UtcNow.AddMonths(-1)).Sum(a => a.amount);
                if ((monthlyTransfers + transferData.amount) > currentLimit.monthlyMaxTransfer)
                {
                    return Ok(new _transferResponse { type = "error", message = _localizer["limitExceededMonthlyMaxTransfer"].Value });
                }

                // Günlük transfer sayısı kontrolü
                var dailyTransferCount = _accountingCollection.AsQueryable().Count(a => a.userId == userId && a.date > DateTime.UtcNow.AddDays(-1));
                if (dailyTransferCount >= currentLimit.dailyMaxTransferCount)
                {
                    return Ok(new _transferResponse { type = "error", message = _localizer["limitExceededDailyMaxTransferCount"].Value });
                }
            }
            var recipientUserId = recipient.userId;

            var senderUpdate = Builders<Wallet>.Update.Inc(w => w.balance, -transferData.amount);
            _walletCollection.UpdateOne(u => u._id == sender._id, senderUpdate);

            var recipientUpdate = Builders<Wallet>.Update.Inc(w => w.balance, transferData.amount);
            _walletCollection.UpdateOne(u => u._id == recipient._id, recipientUpdate);

            var senderAccounting = new Accounting
            {
                userId = sender.userId,
                amount = -transferData.amount,
                currency = sender.currency,
                walletId = sender._id,
                recipientUserId = recipient.userId,
                date = DateTimeOffset.Now
            };
            _accountingCollection.InsertOne(senderAccounting);

            var recipientAccounting = new Accounting
            {
                userId = recipient.userId,
                amount = transferData.amount,
                currency = recipient.currency,
                walletId = transferData.recipientWalletId,
                senderUserId = sender.userId,
                date = DateTimeOffset.Now,

            };
            _accountingCollection.InsertOne(recipientAccounting);

            return Ok(new _transferResponse { type = "success", message = _localizer["5"].Value });
        }
        #endregion

        #region User Financial Report
        public class _financialReportRequest
        {
            public int? pageSize { get; set; } = 10;
            public int? pageNumber { get; set; } = 1;
            public DateTime? startDate { get; set; }
            public DateTime? endDate { get; set; }
            public string walletId { get; set; }
            public string currency { get; set; }
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
                return Ok(new _financialReportResponse { type = "error", message = _localizer["6"].Value });
            }

            ObjectId userIdObj;
            try
            {
                userIdObj = ObjectId.Parse(userIdFromSession);
            }
            catch
            {
                return Ok(new _financialReportResponse { type = "error", message = _localizer["7"].Value });
            }

            if (string.IsNullOrEmpty(request.walletId))
            {
                return Ok(new _financialReportResponse { type = "error", message = _localizer["8"].Value });
            }

            int walletIdInt;
            if (!int.TryParse(request.walletId, out walletIdInt))
            {
                return Ok(new _financialReportResponse { type = "error", message = _localizer["9"].Value });
            }

            var wallet = _walletCollection.AsQueryable().FirstOrDefault(w => w._id == walletIdInt);

            if (wallet == null || wallet.userId != userIdObj)
            {
                return Ok(new _financialReportResponse { type = "error", message = _localizer["10"].Value });
            }

            decimal totalIncome = 0;
            decimal totalExpense = 0;
            decimal netBalance = 0;

            // Sayfalama için doğru değerleri ayarla
            int pageSize = request.pageSize.HasValue && request.pageSize.Value > 0 ? request.pageSize.Value : 10;
            int pageNumber = request.pageNumber.HasValue && request.pageNumber.Value > 0 ? request.pageNumber.Value : 1;
            var skip = (pageNumber - 1) * pageSize;

            var query = _accountingCollection.AsQueryable().Where(a => a.walletId == wallet._id);

            if (request.startDate.HasValue && request.endDate.HasValue)
            {
                query = query.Where(a => a.date >= request.startDate.Value && a.date <= request.endDate.Value);
            }
            else if (!request.startDate.HasValue && !request.endDate.HasValue)
            {
                var endDate = DateTime.UtcNow;
                var startDate = endDate.AddDays(-30);
                query = query.Where(a => a.date >= startDate && a.date <= endDate);
            }

            if (!string.IsNullOrEmpty(request.currency))
            {
                query = query.Where(a => a.currency == request.currency);
            }

            var accountingForWallet = query.Skip(skip).Take(pageSize).ToList();

            foreach (var entry in accountingForWallet)
            {
                if (entry.amount > 0) totalIncome += entry.amount;
                else totalExpense += entry.amount;
            }
            netBalance += wallet.balance;

            return Ok(new _financialReportResponse
            {
                type = "success",
                message = _localizer["11"].Value,
                totalIncome = totalIncome,
                totalExpense = Math.Abs(totalExpense),
                netBalance = netBalance
            });
        }
        #endregion

    }
}