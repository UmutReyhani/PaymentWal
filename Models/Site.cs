using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PaymentWall.Models
{
    public class Site
    {
        public ObjectId _id { get; set; }

        public string name { get; set; }
        public string domain { get; set; }
        public string? detail { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal tax { get; set; }
        public string currency { get; set; } //CHF EUR TRY
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal fees { get; set; }
        public int maxFailedLoginAttempts { get; set; } = 10;
        public string email { get; set; }
        public string phone { get; set; }
        public string currencyIcon { get; set; }
    }
}
