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

        public class TransferRequest
        {
            [Required]
            public int senderWalletId { get; set; }
            [Required]
            public int recipientWalletId { get; set; }
            [Required]
            [BsonRepresentation(BsonType.Decimal128)]
            public decimal Amount { get; set; }
        }

        public class TransferResponse
        {
            public string Type { get; set; } // success / error
            public string Message { get; set; }
        }

        [HttpPost("transfer")]
        public ActionResult<TransferResponse> TransferFunds([FromBody] TransferRequest transferData)
        {
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");

            var sender = _walletCollection.Find(u => u._id == transferData.senderWalletId).FirstOrDefault();
            var recipient = _walletCollection.Find(u => u._id == transferData.recipientWalletId).FirstOrDefault();

            if (sender == null || recipient == null)
            {
                return Ok(new TransferResponse { Type = "error", Message = "Invalid wallet Id." });
            }

            if (sender.balance < transferData.Amount)
            {
                return Ok(new TransferResponse { Type = "error", Message = "Insufficient balance." });
            }

            sender.balance -= transferData.Amount;
            _walletCollection.ReplaceOne(u => u._id == sender._id, sender);

            recipient.balance += transferData.Amount;
            _walletCollection.ReplaceOne(u => u._id == recipient._id, recipient);

            var senderAccounting = new Accounting
            {
                userId = sender.userId,
                amount = -transferData.Amount,
                currency = sender.currency,
                walletId = transferData.senderWalletId
            };
            _accountingCollection.InsertOne(senderAccounting);

            var recipientAccounting = new Accounting
            {
                userId = recipient.userId,
                amount = transferData.Amount,
                currency = recipient.currency,
                walletId = transferData.recipientWalletId
            };
            _accountingCollection.InsertOne(recipientAccounting);

            return Ok(new TransferResponse { Type = "success", Message = "Transfer completed successfully." });
        }
    }
}