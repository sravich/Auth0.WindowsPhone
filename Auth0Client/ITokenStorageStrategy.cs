using System.Threading.Tasks;

namespace Auth0.SDK
{
    public interface ITokenStorageStrategy
    {
        Task<string> Retrieve();

        Task Store(string refreshToken);
    }
}
