using MongoDB.Bson.Serialization.Attributes;

namespace PaymentWall.Models
{
	public class translationProvider
	{
		[BsonId, BsonElement("id")]
		public string id { get; set; }
		public Dictionary<string, string> translation { get; set; }
	}
}
