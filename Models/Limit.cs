using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PaymentWall.Models
{
    public class Limit
    {
        public ObjectId _id { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal maxDeposit { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal minDeposit { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal dailyMaxDeposit { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal monthlyMaxDeposit { get; set; }
    }
}