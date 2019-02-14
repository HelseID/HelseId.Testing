namespace HelseId.Demo
{
    public class TestConfiguration
    {
        public string Authority { get; set; }
        public string TestIdpUrl { get; set; }
        public string ClientId { get; set; }
        public string Secret { get; set; }
        public string RedirectUri { get; set; }
        public string Scope { get; set; }

        public static TestConfiguration Utvikling(string clientId, string scope, string redirectUri, string secret)
        {
            return new TestConfiguration
            {
                Authority = "https://helseid-sts.utvikling.nhn.no/",
                TestIdpUrl = "https://hid-testidp.azurewebsites.net",
                ClientId = clientId,
                RedirectUri = redirectUri,
                Scope = scope,
                Secret = secret
            };
        }
        
        public static TestConfiguration Test(string clientId, string scope, string redirectUri, string secret)
        {
            return new TestConfiguration
            {
                Authority = "https://helseid-sts.test.nhn.no/",
                TestIdpUrl = "https://hid-testidp.azurewebsites.net",
                ClientId = clientId,
                RedirectUri = redirectUri,
                Scope = scope,
                Secret = secret
            };
        }
    }
}
