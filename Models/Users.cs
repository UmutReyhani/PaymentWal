﻿using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;


namespace PaymentWall.Models {
    public class Users
    {
        public ObjectId _id { get; set; }
        public string name { get; set; }
        public string surname { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime birthDate { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTimeOffset register { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTimeOffset lastLogin { get; set; }
        public string email { get; set; }
        public string password { get; set; }
        public int type { get; set; } // 0-1 (personel-business)
        public int status { get; set; } // 0-1-2 (passive-active-banned)
        public bool verified { get; set; }
        public bool emailVerified { get; set; }
        public int failedLoginAttempts { get; set; } = 0;
        public string description { get; set; }
        public string passwordResetToken { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTimeOffset? TokenCreationDate { get; set; }
    }
}