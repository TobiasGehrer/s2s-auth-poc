using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Security.Cryptography.X509Certificates;

var authMode = Environment.GetEnvironmentVariable("AUTH_MODE") ?? "mtls";
var certPath = Environment.GetEnvironmentVariable("TLS_CERT_PATH") ?? "/certs/server.crt";
var keyPath = Environment.GetEnvironmentVariable("TLS_KEY_PATH") ?? "/certs/server.key";
var caBundlePath = Environment.GetEnvironmentVariable("CA_BUNDLE_PATH") ?? "/certs/ca_bundle.crt";
var keycloakUrl = Environment.GetEnvironmentVariable("KEYCLOAK_URL") ?? "http://keycloak:8090";
var realm = Environment.GetEnvironmentVariable("KEYCLOAK_REALM") ?? "s2s-auth";

var builder = WebApplication.CreateBuilder(args);

if (authMode == "mtls")
{
    var caBundle = new X509Certificate2Collection();
    caBundle.ImportFromPemFile(caBundlePath);

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(443, listenOptions =>
        {
            listenOptions.UseHttps(https =>
            {
                https.ServerCertificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
                https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                https.ClientCertificateValidation = (cert, chain, _) =>
                {
                    chain!.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

                    foreach (var ca in caBundle)
                    { 
                        chain.ChainPolicy.CustomTrustStore.Add(ca); 
                    }

                    return chain.Build(cert);
                };
            });
        });
    });

    builder.Services
        .AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
        .AddCertificate(options =>
        {
            options.RevocationMode = X509RevocationMode.NoCheck;
            options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
            options.CustomTrustStore = caBundle;
        });
}
else
{
    var authority = $"{keycloakUrl}/realms/{realm}";

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new()
            {
                ValidateAudience = false
            };
        });
}

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/metrics", (HttpContext ctx) =>
{
    string clientId;
    if (authMode == "mtls")
    {
        clientId = ctx.Connection.ClientCertificate?.Subject ?? "none";
    }
    else
    {
        clientId = ctx.User.Claims.FirstOrDefault(c => c.Type == "azp" || c.Type == "client_id") ?.Value ?? "none";
    }

    return Results.Ok(new
    {
        status = "ok",
        timestamp = DateTime.UtcNow,
        service = "ServiceB",
        authenticated_client = clientId
    });
}).RequireAuthorization();

app.MapGet("/health", () => Results.Ok("healthy")).AllowAnonymous();

app.Run();