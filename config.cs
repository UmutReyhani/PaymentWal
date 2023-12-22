using MongoDB.Driver;
using System.Globalization;

namespace PaymentWall
{
    public class config
    {
        public static readonly List<string> avaibleCurrencies = new List<string> { "CHF", "USD", "TRY" };

        public static string defaultLanguage = "de";
        public static List<CultureInfo> supportedCultures
        {
            get
            {
                var send = new List<CultureInfo>() { };
                send.Add(new CultureInfo("de"));
                send.Add(new CultureInfo("en"));
                return send;
            }
        }

        public static bool isDevelopment = false;
        private static IMongoDatabase db { get; set; }
        public static IMongoDatabase createMapper()
        {
            if (db == null)
            {
                db = _createMapper();
            }

            return db;
        }
        private static IMongoDatabase _createMapper()
        {
            var str = isDevelopment? "mongodb://localhost:27017": "mongodb://paymentWall:paymentWall@10.10.10.61:27017/paymentWall";
            var mongoClient = new MongoClient(str);
            return mongoClient.GetDatabase("paymentWall");
        }
    }
}
