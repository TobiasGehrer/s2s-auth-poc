using System.Security.Cryptography.X509Certificates;

var certPath = Environment.GetEnvironmentVariable("TLS_CERT_PATH") ?? "/certs/client.crt";
var keyPath = Environment.GetEnvironmentVariable("TLS_KEY_PATH") ?? "/certs/client.key";
var caBundlePath = Environment.GetEnvironmentVariable("CA_BUNDLE_PATH") ?? "/certs/ca_bundle.crt";
var serviceBUrl = Environment.GetEnvironmentVariable("SERVICE_B_URL") ?? "https://service-b:443";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("ServiceB", client =>
{
    client.BaseAddress = new Uri(serviceBUrl);
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var clientCert = X509Certificate2.CreateFromPemFile(certPath, keyPath);

    // CA Bundle laden (Root + Intermediate)
    var caBundle = new X509Certificate2Collection();
    caBundle.ImportFromPemFile(caBundlePath);

    var handler = new HttpClientHandler();
    handler.ClientCertificates.Add(clientCert);
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

var app = builder.Build();

app.MapGet("/trigger", async (IHttpClientFactory factory) =>
{
    try
    {
        var client = factory.CreateClient("ServiceB");
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