using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PaymentWall.Models
{
    public class Admin
    {
        public ObjectId _id { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public string password { get; set; }
        //role admin-person
        public string active { get; set; } // 0-1 (passive-active)
        public int failedLoginAttempts { get; set; } = 0;
        [BsonRepresentation(BsonType.DateTime)]
        public DateTimeOffset lastLogin { get; set; }

    }
}
