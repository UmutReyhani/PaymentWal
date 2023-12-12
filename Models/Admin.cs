using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Net.Sockets;
using System.Text;

namespace PaymentWall.Models
{
    public class Admin
    {
        public ObjectId _id { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public string password { get; set; }
        public int role { get; set; }  // 0-person   1-admin
        public int active { get; set; } // 0-1 (passive-active)
        public int failedLoginAttempts { get; set; } = 0;
        [BsonRepresentation(BsonType.DateTime)]
        public DateTimeOffset lastLogin { get; set; }

    }
}
