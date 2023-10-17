using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PaymentWall.Models
{
    public class Financial
    {
        public ObjectId _id { get; set; }
        public ObjectId userId { get; set; }
        public string bank { get; set; }
        public string iban { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal amount { get; set; }
        public string currency { get; set; }
        public string type { get; set; }  // deposit-withdraw
        public string status { get; set; } // 0(peending) 1 (approved) 2  (rejected)
    }
}
