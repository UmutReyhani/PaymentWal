using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PaymentWall.Models
{
    public class Accounting
    {
        public ObjectId _id { get; set; }
        public string userId { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal amount { get; set; }
        public ObjectId? walletId { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal tax { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal fees { get; set; }
        public string currency { get; set; }
      
        public ObjectId? financialId { get; set; }
    }
}
