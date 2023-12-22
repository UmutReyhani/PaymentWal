using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

public class Ticket
{
    [BsonId]
    public ObjectId _id { get; set; }
    public ObjectId userId { get; set; }
    public string title { get; set; }
    public string description { get; set; }
    [BsonRepresentation(BsonType.DateTime)]
    public DateTimeOffset date { get; set; }
    [BsonRepresentation(BsonType.DateTime)]
    public DateTime? dateResolved { get; set; }
    public string adminResponse { get; set; }
    public int status { get; set; } // "Pending","Open", "Resolved"
}
