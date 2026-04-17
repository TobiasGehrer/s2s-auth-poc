using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

var authMode = Environment.GetEnvironmentVariable("AUTH_MODE") ?? "mtls";
var certPath = Environment.GetEnvironmentVariable("TLS_CERT_PATH") ?? "/certs/client.crt";
var keyPath = Environment.GetEnvironmentVariable("TLS_KEY_PATH") ?? "/certs/client.key";
var caBundlePath = Environment.GetEnvironmentVariable("CA_BUNDLE_PATH") ?? "/certs/ca_bundle.crt";
var serviceBUrl = Environment.GetEnvironmentVariable("SERVICE_B_URL") ?? "https://service-b:443";
var keycloakUrl = Environment.GetEnvironmentVariable("KEYCLOAK_URL") ?? "http://keycloak:8090";
var realm = Environment.GetEnvironmentVariable("KEYCLOAK_REALM") ?? "s2s-auth";
var clientId = Environment.GetEnvironmentVariable("CLIENT_ID") ?? "service-a";
var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET") ?? "service-a-secret";

var builder = WebApplication.CreateBuilder(args);

if (authMode == "mtls")
{
    var caBundle = new X509Certificate2Collection();
    caBundle.ImportFromPemFile(caBundlePath);

    builder.Services.AddHttpClient("ServiceB", client =>
    {
        client.BaseAddress = new Uri(serviceBUrl);
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(cert);
        handler.ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
        {
            if (cert == null) return false;
            chain!.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            foreach (var ca in caBundle)
                chain.ChainPolicy.CustomTrustStore.Add(ca);
            return chain.Build(new X509Certificate2(cert));
        };
        return handler;
    });
}
else
{
    // OAuth 2.0: einfacher HttpClient ohne mTLS
    builder.Services.AddHttpClient("ServiceB", client =>
    {
        client.BaseAddress = new Uri(serviceBUrl);
    });

    // Keycloak Token-Client
    builder.Services.AddHttpClient("Keycloak", client =>
    {
        client.BaseAddress = new Uri(keycloakUrl);
    });
}

// Einfacher In-Memory Token Cache
var tokenCache = new TokenCache();
builder.Services.AddSingleton(tokenCache);

var app = builder.Build();

app.MapGet("/trigger", async (IHttpClientFactory factory, TokenCache cache) =>
{
    try
    {
        var client = factory.CreateClient("ServiceB");

        if (authMode == "oauth2")
        {
            var token = await GetTokenAsync(factory, cache);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        var response = await client.GetAsync("/metrics");
        var content = await response.Content.ReadAsStringAsync();
        return Results.Content(content, "application/json");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();

async Task<string> GetTokenAsync(IHttpClientFactory factory, TokenCache cache)
{
    // Gecachtes Token zurückgeben wenn noch gültig
    if (cache.Token != null && cache.ExpiresAt > DateTime.UtcNow.AddSeconds(30))
        return cache.Token;

    var keycloakClient = factory.CreateClient("Keycloak");
    var tokenUrl = $"/realms/{realm}/protocol/openid-connect/token";

    var body = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = clientId,
        ["client_secret"] = clientSecret
    });

    var response = await keycloakClient.PostAsync(tokenUrl, body);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    var doc = JsonDocument.Parse(json);

    var accessToken = doc.RootElement
        .GetProperty("access_token").GetString()!;
    var expiresIn = doc.RootElement
        .GetProperty("expires_in").GetInt32();

    cache.Token = accessToken;
    cache.ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

    return accessToken;
}

// Einfacher In-Memory Token Cache
public class TokenCache
{
    public string? Token { get; set; }
    public DateTime ExpiresAt { get; set; }
}