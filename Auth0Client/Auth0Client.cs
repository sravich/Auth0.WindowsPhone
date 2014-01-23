using Microsoft.Phone.Controls;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Auth0.SDK
{
    /// <summary>
    /// A simple client to Authenticate Users with Auth0.
    /// </summary>
    public partial class Auth0Client
    {
        private const string AuthorizeUrl = "https://{0}/authorize?client_id={1}&scope={2}&redirect_uri={3}&response_type=token&connection={4}";
        private const string LoginWidgetUrl = "https://{0}/login/?client={1}&scope={2}&redirect_uri={3}&response_type=token";
        private const string ResourceOwnerEndpoint = "https://{0}/oauth/ro";
        private const string DelegationEndpoint = "https://{0}/delegation";
        private const string UserInfoEndpoint = "https://{0}/userinfo?access_token={1}";
        private const string DefaultCallback = "https://{0}/mobile";

        private readonly string domain;
        private readonly string clientId;
        
        private readonly AuthenticationBroker broker;

        public Auth0Client(string domain, string clientId)
        {
            this.domain = domain;
            this.clientId = clientId;
            this.broker = new AuthenticationBroker();
        }

        public Auth0User CurrentUser { get; private set; }

        public string CallbackUrl
        {
            get
            {
                return string.Format(DefaultCallback, this.domain);
            }
        }

        internal string State { get; set; }

        /// <summary>
        /// Login a user into an Auth0 application. Attempts to do a background login, but if unsuccessful shows an embedded browser window either showing the widget or skipping it by passing a connection name
        /// </summary>
        /// <param name="connection">Optional connection name to bypass the login widget</param>
        /// <param name="scope">Optional scope, either 'openid' or 'openid profile'</param>
        /// <returns>Returns a Task of Auth0User</returns>
        public async Task<Auth0User> LoginAsync(string connection = "", string scope = "openid")
        {
            // Always make just the basic Authorize call to avoid truncation of profile attributes due to limited URI length in embedded browser
            var startUri = GetStartUri(connection, "openid");
            var expectedEndUri = new Uri(this.CallbackUrl);

            // Attempt a background login for returning users
            var backgroundLoginResult = await DoBackgroundLoginAsync(startUri, expectedEndUri);
            
            var user = backgroundLoginResult.Success
                ? backgroundLoginResult.User
                : await DoInteractiveLoginAsync(backgroundLoginResult.LoginProcessUri, expectedEndUri);
            
            // If scope was specified as 'openid profile', augment basic profile with provider profile attributes
            this.CurrentUser = (scope == "openid profile") ?
                await RetrieveProviderProfileAsync(user) :
                user;

            return this.CurrentUser;
        }

        /// <summary>
        ///  Log a user into an Auth0 application given an user name and password.
        /// </summary>
        /// <returns>Task that will complete when the user has finished authentication.</returns>
        /// <param name="connection" type="string">The name of the connection to use in Auth0. Connection defines an Identity Provider.</param>
        /// <param name="userName" type="string">User name.</param>
        /// <param name="password type="string"">User password.</param>
        /// <param name="scope">Scope.</param>
        public async Task<Auth0User> LoginAsync(string connection, string userName, string password, string scope = "openid")
        {
            var taskFactory = new TaskFactory();

            var endpoint = string.Format(ResourceOwnerEndpoint, this.domain);
            var parameters = String.Format(
                "client_id={0}&connection={1}&username={2}&password={3}&grant_type=password&scope={4}",
                this.clientId,
                connection,
                userName,
                password,
                Uri.EscapeDataString(scope));

            byte[] postData = Encoding.UTF8.GetBytes(parameters);

            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = postData.Length;

            using (var stream = await taskFactory.FromAsync<Stream>(request.BeginGetRequestStream, request.EndGetRequestStream, null))
            {
                await stream.WriteAsync(postData, 0, postData.Length);
                await stream.FlushAsync();
                stream.Close();
            };

            var response = await taskFactory.FromAsync<WebResponse>(request.BeginGetResponse, request.EndGetResponse, null);

            try
            {
                using (Stream responseStream = response.GetResponseStream())
                {
                    using (StreamReader streamReader = new StreamReader(responseStream))
                    {
                        var text = await streamReader.ReadToEndAsync();
                        var data = JObject.Parse(text).ToObject<Dictionary<string, string>>();

                        if (data.ContainsKey("error"))
                        {
                            throw new UnauthorizedAccessException("Error authenticating: " + data["error"]);
                        }
                        else if (data.ContainsKey("access_token"))
                        {
                            this.CurrentUser = new Auth0User(data);
                        }
                        else
                        {
                            throw new UnauthorizedAccessException("Expected access_token in access token response, but did not receive one.");
                        }

                        streamReader.Close();
                    }
                    responseStream.Close();
                }
            }
            catch (Exception)
            {
                throw;
            }

            return this.CurrentUser;
        }

        /// <summary>
        /// Get a delegation token
        /// </summary>
        /// <returns>Delegation token result.</returns>
        /// <param name="targetClientId">Target client ID.</param>
        /// <param name="options">Custom parameters.</param>
        public async Task<JObject> GetDelegationToken(string targetClientId, IDictionary<string, string> options = null)
        {
            var id_token = string.Empty;
            options = options ?? new Dictionary<string, string>();

            // ensure id_token
            if (options.ContainsKey("id_token"))
            {
                id_token = options["id_token"];
                options.Remove("id_token");
            }
            else
            {
                id_token = this.CurrentUser.IdToken;
            }

            if (string.IsNullOrEmpty(id_token))
            {
                throw new InvalidOperationException(
                        "You need to login first or specify a value for id_token parameter.");
            }

            var taskFactory = new TaskFactory();

            var endpoint = string.Format(DelegationEndpoint, this.domain);
            var parameters = String.Format(
                "grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer&id_token={0}&target={1}&client_id={2}",
                id_token,
                targetClientId,
                this.clientId);

            foreach (var option in options)
            {
                if (!string.IsNullOrEmpty(option.Value))
                {
                    parameters += string.Format("&{0}={1}", option.Key, option.Value);
                }
            }

            byte[] postData = Encoding.UTF8.GetBytes(parameters);

            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = postData.Length;

            using (var stream = await taskFactory.FromAsync<Stream>(request.BeginGetRequestStream, request.EndGetRequestStream, null))
            {
                await stream.WriteAsync(postData, 0, postData.Length);
                await stream.FlushAsync();
                stream.Close();
            };

            JObject delegationResult;
            var response = await taskFactory.FromAsync<WebResponse>(request.BeginGetResponse, request.EndGetResponse, null);

            try
            {
                using (Stream responseStream = response.GetResponseStream())
                {
                    using (var streamReader = new StreamReader(responseStream))
                    {
                        var text = await streamReader.ReadToEndAsync();
                        delegationResult = JObject.Parse(text);
                        streamReader.Close();
                    }

                    responseStream.Close();
                }
            }
            catch (Exception)
            {
                throw;
            }

            return delegationResult;
        }

        /// <summary>
        /// Log a user out of a Auth0 application.
        /// </summary>
        public async Task LogoutAsync()
        {
            this.CurrentUser = null;
            await this.broker.Logout();
        }

        /// <summary>
        /// Uses a hidden browser object to perform a background authentication, for re-authentication attemps after initial registration
        /// </summary>
        /// <param name="startUri">Uri pointing to the start of the authentication process</param>
        /// <param name="expectedEndUri">Expected callback Uri at successful completion of authentication process</param>
        /// <returns>Authenticated Auth0User</returns>
        private async Task<BackgroundLoginResult> DoBackgroundLoginAsync(Uri startUri, Uri expectedEndUri)
        {
            Uri endUri = null;
            var resetEvent = new AutoResetEvent(false);
            var backgroundBrowser = new WebBrowser();
            backgroundBrowser.Navigated += (o, e) =>
            {
                endUri = e.Uri;
                resetEvent.Set();
            };
            
            backgroundBrowser.Navigate(startUri);
            await Task.Factory.StartNew(() => resetEvent.WaitOne());
            
            if (endUri == expectedEndUri)
            {
                return new BackgroundLoginResult(broker.GetTokenStringFromResponseData(endUri.ToString()));
            }

            return new BackgroundLoginResult(endUri);
        }

        /// <summary>
        /// Takes over the root frame to display a browser to the user
        /// </summary>
        /// <param name="startUri">Uri pointing to the start of the authentication process</param>
        /// <param name="expectedEndUri">Expected callback Uri at successful completion of authentication process</param>
        /// <returns>Authenticated Auth0User</returns>
        private async Task<Auth0User> DoInteractiveLoginAsync(Uri startUri, Uri expectedEndUri)
        {
            return await this.broker.AuthenticateAsync(startUri, expectedEndUri);
        }

        /// <summary>
        /// Augments an authenticated Auth0User with profile information
        /// </summary>
        /// <param name="user">Authenticated Auth0User</param>
        /// <returns>Authenticated Auth0User populated with profile information</returns>
        private async Task<Auth0User> RetrieveProviderProfileAsync(Auth0User user)
        {
            var userProfileEndpoint = string.Format(UserInfoEndpoint, this.domain, user.Auth0AccessToken);
            var userProfileRequest = (HttpWebRequest)WebRequest.Create(userProfileEndpoint);
            userProfileRequest.Method = "GET";

            var taskFactory = new TaskFactory();
            var response = await taskFactory.FromAsync<WebResponse>(userProfileRequest.BeginGetResponse, userProfileRequest.EndGetResponse, null);

            using (var responseStream = response.GetResponseStream())
            {
                using (var streamReader = new StreamReader(responseStream))
                {
                    var text = streamReader.ReadToEnd();
                    var profileJsonObject = JObject.Parse(text);
                    
                    // Augment with extra user profile attributes from provider
                    foreach (var item in profileJsonObject.Properties())
                    {
                        user.Profile.Add(item.Name, item.Value);
                    }
                        
                    streamReader.Close();
                }

                responseStream.Close();
            }

            return user;
        }

        private Uri GetStartUri(string connection, string scope)
        {
            // Generate state to include in startUri
            var chars = new char[16];
            var rand = new Random();
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)rand.Next((int)'a', (int)'z' + 1);
            }

            var authorizeUri = !string.IsNullOrWhiteSpace(connection) ?
                string.Format(AuthorizeUrl, domain, clientId, Uri.EscapeDataString(scope), Uri.EscapeDataString(this.CallbackUrl), connection) :
                string.Format(LoginWidgetUrl, domain, clientId, Uri.EscapeDataString(scope), Uri.EscapeDataString(this.CallbackUrl));

            this.State = new string(chars);
            var startUri = new Uri(authorizeUri + "&state=" + this.State);

            return startUri;
        }

        private class BackgroundLoginResult
        {
            internal BackgroundLoginResult(Auth0User user)
            {
                User = user;
                Success = true;
            }

            internal BackgroundLoginResult(Uri loginUri)
            {
                LoginProcessUri = loginUri;
                Success = false;
            }

            public Auth0User User { get; private set; }

            public Uri LoginProcessUri { get; private set; }

            public bool Success { get; private set; }
        }
    }
}
