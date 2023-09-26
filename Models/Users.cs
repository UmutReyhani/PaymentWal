﻿using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;


namespace PaymentWall.Models {
    public class Users
    {
        public ObjectId _id { get; set; }
        public string walletId { get; set; }
        public string name { get; set; }
        public string surname { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime birthDate { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTimeOffset register { get; set; }
        public DateTimeOffset? lastLogin { get; set; }
        public string email { get; set; }
        public string password { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal balance { get; set; }
        public string currency { get; set; }
        public string type { get; set; } // 0-1 (personel-business)
        public string status { get; set; } // 0-1 (passive-active)
        public bool verified { get; set; }
        public bool emailVerified { get; set; }
        public int failedLoginAttempts { get; set; } = 0;
    }
}