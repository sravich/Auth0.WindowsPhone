using System.IO;
#if WINDOWS_PHONE
using System.IO.IsolatedStorage;
#endif
using System.Threading.Tasks;
namespace Auth0.SDK
{
    internal class IsolatedStorageTokenStorageStrategy : ITokenStorageStrategy
    {
        private readonly string fileName;

        public IsolatedStorageTokenStorageStrategy(string identifier)
        {
            this.fileName = string.Format("{0}.txt", identifier);
        }

        public async Task Store(string refreshToken)
        {
            using (IsolatedStorageFile isoStore = IsolatedStorageFile.GetUserStoreForApplication())
            {
                using (var file = isoStore.OpenFile(this.fileName, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    using (StreamWriter writer = new StreamWriter(file))
                    {
                        await writer.WriteAsync(refreshToken);
                    }
                }
            }       
        }

        public async Task<string> Retrieve()
        {
            using (IsolatedStorageFile isoStore = IsolatedStorageFile.GetUserStoreForApplication())
            {
                if (isoStore.FileExists(this.fileName))
                {
                    using (var file = isoStore.OpenFile(this.fileName, FileMode.Open, FileAccess.Read))
                    {
                        using (StreamReader reader = new StreamReader(file))
                        {
                            return await reader.ReadToEndAsync();
                        }
                    }
                }
            }

            return string.Empty;
        }
    }
}
