using System.Text;
using AzureToAWS.Api.Configuration;
using AzureToAWS.Api.Services;
using AzureToAWS.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AzureToAWS.Api;

public class Startup
{
    public IConfiguration Configuration { get; }
    public Startup(IConfiguration configuration) => Configuration = configuration;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // IMPORTANT: single registration of resolver
        services.AddSingleton<IConnectionStringResolver, ConnectionStringResolver>();

        // Default context (US writer) – optional; your controller builds country-specific contexts manually
        services.AddDbContext<ApplicationDbContext>((sp, opts) =>
        {
            var resolver = sp.GetRequiredService<IConnectionStringResolver>();
            opts.UseNpgsql(resolver.GetWriter("US"));
        });

        services.AddMemoryCache();
        services.AddAuthorization();

        services.AddSingleton<IJwtKeyProvider, PgJwtKeyProvider>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ISqsService, SqsService>();
        services.AddScoped<IUserAccessService, PgUserAccessService>();

        services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(o =>
        {
            o.RequireHttpsMetadata = false;
            o.SaveToken = true;
            o.TokenValidationParameters = new()
            {
                ValidateIssuerSigningKey = true,
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero,
                IssuerSigningKeyResolver = (token, securityToken, kid, p) =>
                {
                    var sp = services.BuildServiceProvider();
                    var keyProvider = sp.GetRequiredService<IJwtKeyProvider>();
                    return new[] { keyProvider.GetSigningKey() };
                }
            };
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(e => e.MapControllers());
    }
}