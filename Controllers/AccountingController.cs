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
        [CheckAdminLogin]
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

            var senderUpdate = Builders<Wallet>.Update.Set(w => w.balance, sender.balance - transferData.amount);//$inc
            _walletCollection.UpdateOne(u => u._id == sender._id, senderUpdate);

            var recipientUpdate = Builders<Wallet>.Update.Set(w => w.balance, recipient.balance + transferData.amount);
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
    }
}