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
        public string currency { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal fees { get; set; }
        public int maxFailedLoginAttempts { get; set; } = 10;
    }
}
