using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Adp.YnabClient.Ynab;

public sealed class Oauth( IConfiguration configuration )
{
    private const string BaseYnabUri = "https://app.youneedabudget.com/oauth/";
    private readonly HttpClient client = new();
    private readonly string redirectUri = configuration.GetValue< string >( "YNAB_REDIRECT_URI" );
    private readonly string ynabClientId = configuration.GetValue< string >( "YNAB_CLIENT_ID" );
    private readonly string ynabClientSecret = configuration.GetValue< string >( "YNAB_CLIENT_SECRET" );

    public string GetAuthLink() => $"{BaseYnabUri}authorize?client_id={ynabClientId}&redirect_uri={redirectUri}&response_type=code";

    public AccessTokenInfo GetAccessToken( string authCode )
    {
        var values = new Dictionary< string, string >
                     {
                         { "client_id", ynabClientId }, { "client_secret", ynabClientSecret }, { "redirect_uri", redirectUri }, { "grant_type", "authorization_code" }, { "code", authCode },
                     };
        var content = new FormUrlEncodedContent( values );

        const string authUrl = $"{BaseYnabUri}token";

        var response = client.PostAsync( authUrl, content ).Result;
        var responseStr = response.Content.ReadAsStringAsync().Result;
        return JsonConvert.DeserializeObject< AccessTokenInfo >( responseStr );
    }

    public AccessTokenInfo GetRefreshedAccessToken( string refreshToken )
    {
        var values = new Dictionary< string, string > { { "client_id", ynabClientId }, { "client_secret", ynabClientSecret }, { "grant_type", "refresh_token" }, { "refresh_token", refreshToken } };
        var content = new FormUrlEncodedContent( values );

        const string authUrl = $"{BaseYnabUri}token";

        var response = client.PostAsync( authUrl, content ).Result;
        var responseStr = response.Content.ReadAsStringAsync().Result;
        return JsonConvert.DeserializeObject< AccessTokenInfo >( responseStr );
    }

    public sealed class AccessTokenInfo
    {
        // ReSharper disable InconsistentNaming
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public string refresh_token { get; set; }
        public string scope { get; set; }

        public int created_at { get; set; }
        // ReSharper restore InconsistentNaming
    }
}