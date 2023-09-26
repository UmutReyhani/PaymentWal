using MongoDB.Driver;

namespace PaymentWall.Services
{
    public interface IConnectionService
    {
        IMongoDatabase db();
    }
}
