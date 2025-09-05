using Amazon.SQS;
using CopyAzureToAWS.Api;
using CopyAzureToAWS.Api.Configuration;
using CopyAzureToAWS.Api.Infrastructure.Logging;
using CopyAzureToAWS.Api.Services;
using CopyAzureToAWS.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;

var builder = WebApplication.CreateBuilder(args);

// Logging (optional explicit providers)
builder.Logging.AddConsole();

// Add framework services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddAuthorization();

// EF Core
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WriterConnection")));

// Options binding for SQS
builder.Services.Configure<SqsOptions>(opts =>
{
    opts.QueueUrl = builder.Configuration["AWS:Sqs:QueueUrl"]
                    ?? builder.Configuration["AWS:Sqs:QueueUrl"]
                    ?? Environment.GetEnvironmentVariable("AWS__SQS__QueueUrl");
    opts.Region = builder.Configuration["Sqs:Region"];
});

// Custom services
builder.Services.AddSingleton<IJwtKeyProvider, PgJwtKeyProvider>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ISqsService, SqsService>();
builder.Services.AddScoped<IUserAccessService, PgUserAccessService>();
builder.Services.AddApiServices(builder.Configuration);

// JWT auth (unchanged)
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

var logger = builder.Logging.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
//logger.WriteLog("Version", $"Starting CopyAzureToAWS.Api version:", "TEST", true);
Console.WriteLine("VERSION:1.0.0.0");

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
