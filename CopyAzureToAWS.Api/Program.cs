using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using CopyAzureToAWS.Data;
using CopyAzureToAWS.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddAuthorization();

// Add Entity Framework (PostgreSQL) - default to writer
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WriterConnection")));

// Register custom services
builder.Services.AddSingleton<IJwtKeyProvider, PgJwtKeyProvider>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ISqsService, SqsService>();
builder.Services.AddScoped<IUserAccessService, PgUserAccessService>();

// Resolve signing key from DB (PostgreSQL)
using (var sp = builder.Services.BuildServiceProvider())
{
    var keyProvider = sp.GetRequiredService<IJwtKeyProvider>();
    var signingKey = keyProvider.GetSigningKey();

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });
}

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
