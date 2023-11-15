using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

public class Ticket
{
    [BsonId]
    public ObjectId _id { get; set; }
    public ObjectId userId { get; set; }
    public string title { get; set; }
    public string description { get; set; }
    public DateTimeOffset dateCreated { get; set; }
    public DateTime? dateResolved { get; set; }
    public string adminResponse { get; set; }
    public int status { get; set; } = 0; // "Pending","Open", "Resolved"
}
