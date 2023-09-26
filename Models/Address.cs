using MongoDB.Bson;
namespace PaymentWall.Models
{
    public class Address
    {
        public ObjectId _id { get; set; }
        public ObjectId userId { get; set; }
        public string address { get; set; }
        public string city { get; set; }
        public string postCode { get; set; }
        public string country { get; set; }
        public string phoneNumber { get; set; }
    }
}
