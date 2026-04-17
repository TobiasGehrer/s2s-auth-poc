using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Security.Cryptography.X509Certificates;

var certPath = Environment.GetEnvironmentVariable("TLS_CERT_PATH") ?? "/certs/server.crt";
var keyPath = Environment.GetEnvironmentVariable("TLS_KEY_PATH") ?? "/certs/server.key";
var caBundlePath = Environment.GetEnvironmentVariable("CA_BUNDLE_PATH") ?? "/certs/ca_bundle.crt";

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(443, listenOptions =>
    {
        listenOptions.UseHttps(https =>
        {
            https.ServerCertificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            https.ClientCertificateValidation = (cert, chain, errors) =>
            {
                // CA Bundle laden (Root + Intermediate)
                var caBundle = new X509Certificate2Collection();
                caBundle.ImportFromPemFile(caBundlePath);

                chain!.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

                foreach (var ca in caBundle)
                    chain.ChainPolicy.CustomTrustStore.Add(ca);

                return chain.Build(cert);
            };
        });
    });
});

var caBundle = new X509Certificate2Collection();
caBundle.ImportFromPemFile(caBundlePath);

builder.Services
    .AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
    .AddCertificate(options =>
    {
        options.RevocationMode = X509RevocationMode.NoCheck;
        options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
        options.CustomTrustStore = caBundle;
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/metrics", (HttpContext ctx) =>
{
    var clientCert = ctx.Connection.ClientCertificate;
    return Results.Ok(new
    {
        status = "ok",
        timestamp = DateTime.UtcNow,
        service = "ServiceB",
        authenticated_client = clientCert?.Subject ?? "none"
    });
}).RequireAuthorization();

app.MapGet("/health", () => Results.Ok("healthy")).AllowAnonymous();

app.Run();