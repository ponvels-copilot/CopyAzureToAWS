using System.Text;
using CopyAzureToAWS.Api.Services;
using CopyAzureToAWS.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CopyAzureToAWS.Api;

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddMemoryCache();
        services.AddAuthorization();

        // EF Core (PostgreSQL) - default to writer connection
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(Configuration.GetConnectionString("WriterConnection")));

        // DI registrations
        services.AddSingleton<IJwtKeyProvider, PgJwtKeyProvider>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ISqsService, SqsService>();
        services.AddScoped<IUserAccessService, PgUserAccessService>();

        // JWT auth configured to resolve key via IJwtKeyProvider
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;

            // Resolve signing key at validation time to avoid building a temporary provider
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero,
                IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                {
                    var keyProvider = services.BuildServiceProvider().GetRequiredService<IJwtKeyProvider>();
                    return new[] { keyProvider.GetSigningKey() };
                }
            };
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseSwagger();
        app.UseSwaggerUI();

        // Must come before auth
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}