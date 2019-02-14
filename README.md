# HelseId.Testing

A simlpe library for getting AccessTokens from HelseId using the Test IDP. 


## Eksempel på bruk

```csharp
var clientId = "Testklient";
var scope = "openid profile helseid://scopes/identity/security_level helseid://scopes/identity/pid";
var redirectUri = "http://localhost:44444";
var secret = "shared secret";

var config = TestConfiguration.Utvikling(clientId, scope, redirectUri, secret);
var client = new TestClient(config);
var accessToken = await client.GetAccessToken("24019491036");
```

For å bruke biblioteket må du ha en gyldig konfigurasjon satt opp i riktig miljø (Utvikling eller Test). 
Etter du har mottatt et Access Token kan du bruke dette for å kalle de tjenestene du har behov for.