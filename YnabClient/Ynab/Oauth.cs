using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Adp.YnabClient.Ynab
{
    public class Oauth
    {
        private readonly string baseYnabUri;
        private readonly HttpClient client = new HttpClient();
        private readonly string redirectUri;
        private readonly string ynabClientId;
        private readonly string ynabClientSecret;

        public Oauth(IConfiguration configuration)
        {
            ynabClientId = configuration.GetValue<string>("YNAB_CLIENT_ID");
            ynabClientSecret = configuration.GetValue<string>("YNAB_CLIENT_SECRET");
            redirectUri = configuration.GetValue<string>("YNAB_REDIRECT_URI");

            baseYnabUri = "https://app.youneedabudget.com/oauth/";
        }

        public string GetAuthLink()
        {
            return $"{baseYnabUri}authorize?client_id={ynabClientId}&redirect_uri={redirectUri}&response_type=code";
        }

        public AccessTokenInfo GetAccessToken(string authCode)
        {
            var values = new Dictionary<string, string>
            {
                {"client_id", ynabClientId},
                {"client_secret", ynabClientSecret},
                {"redirect_uri", redirectUri},
                {"grant_type", "authorization_code"},
                {"code", authCode}
            };
            var content = new FormUrlEncodedContent(values);

            var authUrl = $"{baseYnabUri}token";

            var response = client.PostAsync(authUrl, content).Result;
            var responseStr = response.Content.ReadAsStringAsync().Result;
            return JsonConvert.DeserializeObject<AccessTokenInfo>(responseStr);
        }

        public AccessTokenInfo GetRefreshedAccessToken(string refreshToken)
        {
            var values = new Dictionary<string, string> {{"client_id", ynabClientId}, {"client_secret", ynabClientSecret}, {"grant_type", "refresh_token"}, {"refresh_token", refreshToken}};
            var content = new FormUrlEncodedContent(values);

            var authUrl = $"{baseYnabUri}token";

            var response = client.PostAsync(authUrl, content).Result;
            var responseStr = response.Content.ReadAsStringAsync().Result;
            return JsonConvert.DeserializeObject<AccessTokenInfo>(responseStr);
        }

        public sealed class AccessTokenInfo
        {
            public string access_token { get; set; }
            public string token_type { get; set; }
            public int expires_in { get; set; }
            public string refresh_token { get; set; }
            public string scope { get; set; }
            public int created_at { get; set; }
        }
    }
}