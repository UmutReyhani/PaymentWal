using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PaymentWall.Models
{
    public class Limit
    {
        public ObjectId _id { get; set; }

        // Yatırma limitleri
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal maxDeposit { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal minDeposit { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal dailyMaxDeposit { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal monthlyMaxDeposit { get; set; }

        // Çekim limitleri (walletten -> banka hesabına)
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal maxWithdrawal { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal minWithdrawal { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal dailyMaxWithdrawal { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal monthlyMaxWithdrawal { get; set; }

        // Wallet-wallet para transfer limitleri
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal maxTransfer { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal minTransfer { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal dailyMaxTransfer { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal monthlyMaxTransfer { get; set; }
        public int dailyMaxTransferCount { get; set; }
    }
}
