using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PaymentWall.Models
{
    public class AdminLog
    {
        public ObjectId _id { get; set; }
        public ObjectId userId { get; set; }
        [BsonRepresentation(BsonType.DateTime)]
        public DateTimeOffset date { get; set; }
        public string userAgent { get; set; }
        public string ip { get; set; }
        public int type { get; set; }  // register-login-logout-update-delete(0-1-2-3-4)
        public int? previousStatus { get; set; }
        public int updatedStatus { get; set; }
        public string reason { get; set; }
        public int role { get; set; }
        public ObjectId adminId { get; set; }
        public ObjectId? ticketId { get; set; }
        public int? previousTicketStatus { get; set; }
        public int? updatedTicketStatus { get; set; }

    }
}
