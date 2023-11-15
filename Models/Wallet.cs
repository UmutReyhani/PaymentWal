using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace PaymentWall.Models
{
    public class Wallet
    {
        [BsonId]
        public int _id { get; set; }
        public ObjectId userId { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal balance { get; set; }
        public string currency { get; set; }
        public int status { get; set; }
    }
}
