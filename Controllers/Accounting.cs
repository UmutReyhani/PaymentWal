using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using PaymentWall.Models;
using PaymentWall.Services;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

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
        #region 
        public class TransferRequest
        {
            [Required]
            public string senderWalletId { get; set; } // Gönderenin cüzdan Id
            [Required]
            public string recipientWalletId { get; set; } // Alıcının cüzdan Id
            [Required]
            [BsonRepresentation(BsonType.Decimal128)]
            public decimal Amount { get; set; } // Transfer edilmek istenen miktar
        }

        public class TransferResponse
        {
            public string Type { get; set; } // success / error
            public string Message { get; set; }
        }

        [HttpPost("transfer")]
        public ActionResult<TransferResponse> TransferFunds([FromBody] TransferRequest transferData)
        {

            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");

            var sender = _userCollection.Find(u => u.walletId == transferData.senderWalletId).FirstOrDefault();
            var recipient = _userCollection.Find(u => u.walletId == transferData.recipientWalletId).FirstOrDefault();

            if (sender == null || recipient == null)
            {
                return Ok(new TransferResponse { Type = "error", Message = "Invalid wallet Id." });
            }

            if (sender.balance < transferData.Amount)
            {
                return Ok(new TransferResponse { Type = "error", Message = "Insufficient balance." });
            }

            // gönderen balance update
            sender.balance -= transferData.Amount;
            _userCollection.ReplaceOne(u => u._id == sender._id, sender);

            // gelen balance update
            recipient.balance += transferData.Amount;
            _userCollection.ReplaceOne(u => u._id == recipient._id, recipient);

            // Accounting kaydı gönderen
            var senderAccounting = new Accounting
            {
                userId = sender._id.ToString(),
                amount = -transferData.Amount,
                currency = sender.currency,
                walletId = ObjectId.Parse(transferData.senderWalletId)
            };
            _accountingCollection.InsertOne(senderAccounting);

            // Accounting kaydı alan
            var recipientAccounting = new Accounting
            {
                userId = recipient._id.ToString(),
                amount = transferData.Amount,
                currency = recipient.currency,
                walletId = ObjectId.Parse(transferData.recipientWalletId)
            };
            _accountingCollection.InsertOne(recipientAccounting);

            return Ok(new TransferResponse { Type = "success", Message = "Transfer completed successfully." });
        }
        #endregion
    }
}

