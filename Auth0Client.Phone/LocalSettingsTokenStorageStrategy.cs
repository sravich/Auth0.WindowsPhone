using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Auth0.SDK
{
    internal class LocalSettingsTokenStorageStrategy : ITokenStorageStrategy
    {
        private readonly string identifier;

        public LocalSettingsTokenStorageStrategy(string identifier)
        {
            this.identifier = identifier;
        }

        public Task<string> Retrieve()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            object token;
            if (localSettings.Values.TryGetValue(identifier, out token))
            {
                return Task.FromResult((string)token);
            }

            return Task.FromResult(string.Empty);
        }

        public Task Store(string refreshToken)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values[identifier] = refreshToken;

            return Task.FromResult<object>(null);
        }
    }
}
