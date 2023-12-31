﻿using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PaymentWall.Models
{
    public class Log
    {
        public ObjectId _id { get; set; }

        public ObjectId userId { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTimeOffset date { get; set; }
        public string userAgent { get; set; }
        public string ip { get; set; }
        public int type { get; set; }  // register-login-logout(0-1-2)
    }
}
