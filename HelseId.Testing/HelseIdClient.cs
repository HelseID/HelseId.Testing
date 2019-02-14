using HtmlAgilityPack;
using IdentityModel;
using IdentityModel.Client;
using IdentityModel.OidcClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace HelseId.Demo
{
    public class TestClient
    {
        readonly TestConfiguration configuration;
        readonly HttpClient clientWithRedirect;
        readonly HttpClient clientNoRedirect;
        readonly CookieContainer cookieContainer;

        public TestClient(TestConfiguration configuration)
        {
            this.configuration = configuration;

            cookieContainer = new CookieContainer();

            clientWithRedirect = new HttpClient(new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = cookieContainer,
                AllowAutoRedirect = true
            });

            clientNoRedirect = new HttpClient(new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = cookieContainer,
                AllowAutoRedirect = false
            });
        }

        public async Task<string> GetAccessToken(string pid, int securityLevel = 4, string hprNumber = "")
        {
            var options = new OidcClientOptions
            {
                Authority = configuration.Authority,
                ClientId = configuration.ClientId,
                Flow = OidcClientOptions.AuthenticationFlow.AuthorizationCode,
                RedirectUri = configuration.RedirectUri,
                Scope = configuration.Scope,
            };

            var oidcClient = new OidcClient(options);
            var authorizeState = await oidcClient.PrepareLoginAsync();

            // Start logon - gå til STS HRD
            var testIdpUrl = await GetTestIdpUrl(authorizeState.StartUrl);

            // Gå til test idp
            var testIdpLogonRequest = await GetTestIdpLogonRequest(testIdpUrl, pid, securityLevel.ToString(), hprNumber);

            // Utfør logon med Test Idp
            var postbackRequest = await PerformTestIdpLogon(testIdpLogonRequest);

            var authCode = await PerformPostbackAndGetAuthCode(postbackRequest);

            return await PerformAccessTokenRequest(authCode, authorizeState.CodeVerifier);
        }
               
        private async Task<HttpRequestMessage> GetTestIdpLogonRequest(string testIdpUrl, string pid, string securityLevel, string hprNumber)
        {
            var testIdpLogonResponse = await clientNoRedirect.GetAsync(testIdpUrl);
            var responseStream = await HandleResponse(testIdpLogonResponse, clientNoRedirect, cookieContainer, configuration.Authority);

            var testIdpHtml = new HtmlDocument();
            testIdpHtml.Load(responseStream);

            var form = testIdpHtml.DocumentNode.SelectSingleNode("//form");

            var formAction = configuration.TestIdpUrl + WebUtility.HtmlDecode(form.Attributes["action"].Value);
            var requestVerificationToken = form.SelectNodes("//input").Where(n => n.Attributes["name"].Value == "__RequestVerificationToken").Single().Attributes["value"].Value;

            var postContent = new FormUrlEncodedContent(new[]
            {
                    new KeyValuePair<string,string>("Pid", pid),
                    new KeyValuePair<string,string> ("SecurityLevel", securityLevel),
                    new KeyValuePair<string,string> ("HprNumber", hprNumber),
                    new KeyValuePair<string,string> ("__RequestVerificationToken", requestVerificationToken)
            });

            var testIdpLogonRequest = new HttpRequestMessage(HttpMethod.Post, formAction);
            testIdpLogonRequest.Content = postContent;
            testIdpLogonRequest.Headers.Add("RequestVerificationToken", requestVerificationToken);

            return testIdpLogonRequest;
        }

        private async Task<string> GetTestIdpUrl(string startUrl)
        {
            var startRequest = await clientWithRedirect.GetAsync(startUrl);

            var htmlDocument = new HtmlDocument();
            htmlDocument.Load(await startRequest.Content.ReadAsStreamAsync());

            var testIdpLink = htmlDocument.GetElementbyId("Test IDP");
            var testIdpUrl = configuration.Authority + WebUtility.HtmlDecode(testIdpLink.Attributes["href"].Value);
            return testIdpUrl;
        }

        private async Task<HttpRequestMessage> PerformTestIdpLogon(HttpRequestMessage testIdpLogonRequest)
        {
            var logonResponse = await clientNoRedirect.SendAsync(testIdpLogonRequest);
            var responseStream = await HandleResponse(logonResponse, clientNoRedirect, cookieContainer, configuration.TestIdpUrl);

            var testIdpPostBackHtml = new HtmlDocument();
            testIdpPostBackHtml.Load(responseStream);

            var postBackForm = testIdpPostBackHtml.DocumentNode.SelectNodes("//form").Single();

            var postBackAction = WebUtility.HtmlDecode(postBackForm.Attributes["action"].Value);
            var postBackInputs = postBackForm.SelectNodes("//input").ToList();
            var code = postBackInputs.Where(i => i.Attributes["name"].Value == "code").Single().Attributes["value"].Value;
            var idtoken = postBackInputs.Where(i => i.Attributes["name"].Value == "id_token").Single().Attributes["value"].Value;
            var scope = postBackInputs.Where(i => i.Attributes["name"].Value == "scope").Single().Attributes["value"].Value;
            var state = postBackInputs.Where(i => i.Attributes["name"].Value == "state").Single().Attributes["value"].Value;
            var sessionState = postBackInputs.Where(i => i.Attributes["name"].Value == "session_state").Single().Attributes["value"].Value;

            var postBackContent = new FormUrlEncodedContent(new[]
            {
                    new KeyValuePair<string,string>("code", code),
                    new KeyValuePair<string,string>("id_token", idtoken),
                    new KeyValuePair<string,string>("scope", scope),
                    new KeyValuePair<string,string>("state", state),
                    new KeyValuePair<string,string>("session_state", sessionState),
            });

            var postBackRequest = new HttpRequestMessage(HttpMethod.Post, postBackAction);
            postBackRequest.Content = postBackContent;

            return postBackRequest;
        }

        private async Task<string> PerformPostbackAndGetAuthCode(HttpRequestMessage postbackRequest)
        {
            var postbackResponse = await clientNoRedirect.SendAsync(postbackRequest);

            var responseStream = await HandleResponse(postbackResponse, clientNoRedirect, cookieContainer, configuration.Authority);

            var tokenPostHtml = new HtmlDocument();
            tokenPostHtml.Load(responseStream);

            var codeElement = tokenPostHtml
                .DocumentNode
                .SelectNodes("//input")
                .Single(n => n.Attributes["name"].Value == "code");

            var codeValue = codeElement.Attributes["value"].Value;

            return codeValue;
        }

        private async Task<string> PerformAccessTokenRequest(string authCode, string codeVerifier)
        {
            var result = await clientNoRedirect.RequestAuthorizationCodeTokenAsync(new AuthorizationCodeTokenRequest
            {
                Address = configuration.Authority + "/connect/token",
                ClientId = configuration.ClientId,
                ClientSecret = configuration.Secret,
                Code = authCode,
                GrantType = OidcConstants.GrantTypes.AuthorizationCode,
                RedirectUri = configuration.RedirectUri,
                CodeVerifier = codeVerifier
            });

            return result.AccessToken;
        }

        private static async Task<Stream> HandleResponse(HttpResponseMessage response, HttpClient client, CookieContainer cookieContainer, string baseAddress)
        {
            while (response.StatusCode == HttpStatusCode.Found)
            {
                if (response.Headers.TryGetValues("set-cookie", out var cookieHeaders))
                {
                    foreach (var cookie in CookieParser.Parse(cookieHeaders))
                    {
                        if (string.IsNullOrEmpty(cookie.Domain))
                        {
                            var domain = new Uri(baseAddress).Authority.Split(':')[0];
                            cookie.Domain = domain;
                        }
                        cookieContainer.Add(cookie);
                    }
                }
                var location = response.Headers.GetValues("location").First();
                if (!location.StartsWith("http"))
                {
                    location = baseAddress + location;
                }

                response = await client.GetAsync(location);
            }

            return await response.Content.ReadAsStreamAsync();
        }
    }
}
