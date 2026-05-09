using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Zafiro.Sync.Api.Security;

public static class ZafiroSyncAuthenticationExtensions
{
    public static IServiceCollection AddZafiroSyncAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AppIdentityAuthenticationOptions>(configuration.GetSection("Authentication:AppIdentity"));
        services.AddSingleton<AppIdentityChallengeStore>();
        services.AddSingleton<AppIdentityTokenService>();

        if (configuration.GetValue("Authentication:Demo:Enabled", false))
        {
            var accessToken = configuration["Authentication:Demo:AccessToken"];
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Authentication:Demo:AccessToken is required when demo authentication is enabled.");
            }

            services.AddAuthentication(DemoAuthenticationDefaults.Scheme)
                .AddScheme<DemoAuthenticationOptions, DemoAuthenticationHandler>(
                    DemoAuthenticationDefaults.Scheme,
                    options =>
                    {
                        options.AccessToken = accessToken;
                        options.Subject = configuration["Authentication:Demo:Subject"] ?? DemoAuthenticationDefaults.Subject;
                        options.AuthorizedParty = configuration["Authentication:Demo:AuthorizedParty"] ?? DemoAuthenticationDefaults.AuthorizedParty;
                    });

            return services;
        }

        var authority = configuration["Authentication:Authority"];
        var audience = configuration["Authentication:Audience"];
        var oidcConfigured = !string.IsNullOrWhiteSpace(authority) && !string.IsNullOrWhiteSpace(audience);

        if (!oidcConfigured)
        {
            services.AddAuthentication(AppIdentityAuthenticationDefaults.Scheme)
                .AddScheme<AppIdentityAuthenticationOptions, AppIdentityAuthenticationHandler>(
                    AppIdentityAuthenticationDefaults.Scheme,
                    options => configuration.GetSection("Authentication:AppIdentity").Bind(options));

            return services;
        }

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AppIdentityAuthenticationDefaults.PolicyScheme;
                options.DefaultChallengeScheme = AppIdentityAuthenticationDefaults.PolicyScheme;
            })
            .AddPolicyScheme(AppIdentityAuthenticationDefaults.PolicyScheme, null, options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var token = ReadBearerToken(context);
                    return token is not null && token.StartsWith(AppIdentityAuthenticationDefaults.TokenPrefix, StringComparison.Ordinal)
                        ? AppIdentityAuthenticationDefaults.Scheme
                        : JwtBearerDefaults.AuthenticationScheme;
                };
            })
            .AddScheme<AppIdentityAuthenticationOptions, AppIdentityAuthenticationHandler>(
                AppIdentityAuthenticationDefaults.Scheme,
                options => configuration.GetSection("Authentication:AppIdentity").Bind(options))
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = configuration.GetValue("Authentication:RequireHttpsMetadata", true);
            });

        return services;
    }

    private static string? ReadBearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }
}
