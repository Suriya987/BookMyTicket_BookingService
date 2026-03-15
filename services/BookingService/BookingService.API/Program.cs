using BookingService.API.Data;
using BookingService.API.Infrastructure.Http;
using BookingService.API.Infrastructure.Kafka;
using BookingService.API.Infrastructure.Redis;
using BookingService.API.Middleware;
using BookingService.API.Repositories;
using BookingService.API.Services;
using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "BookingService")
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

// ── Controllers ───────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "BookMyTicket — Booking Service",
        Version = "v1"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        Description  = "Enter your JWT token"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id   = "Bearer"
            }
        }] = Array.Empty<string>()
    });
});

// ── SQL Server ────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<BookingDbContext>(opt =>
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("BookingDb"),
        sql => sql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null)));

// ── Redis ─────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")!));

// ── Kafka Producer ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers  = builder.Configuration["Kafka:BootstrapServers"],
        Acks              = Acks.All,
        EnableIdempotence = true,
        CompressionType   = CompressionType.Snappy
    }).Build());

// ── HTTP Clients ──────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<IInventoryClient, InventoryClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Services:InventoryServiceUrl"]!);
    c.Timeout     = TimeSpan.FromSeconds(30);
});

// ── DI Registrations ──────────────────────────────────────────────────────────
builder.Services.AddScoped<IBookingRepository,    BookingRepository>();
builder.Services.AddScoped<IBookingService,       BookingService.API.Services.BookingService>();
builder.Services.AddSingleton<IRedisLockService,  RedisLockService>();
builder.Services.AddSingleton<IKafkaPublisherService, KafkaPublisherService>();
builder.Services.AddSingleton<IPaymentClient,     StripePaymentClient>();

// ── JWT Auth ──────────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = builder.Configuration["Jwt:Issuer"],
        ValidAudience            = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey         = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
    });

builder.Services.AddAuthorization();

// ── Health Checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("BookingDb")!,
        name: "sql",
        tags: ["ready"])
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        tags: ["ready"]);

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Middleware Pipeline ───────────────────────────────────────────────────────
app.UseMiddleware<ExceptionMiddleware>();
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI(c =>
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Booking Service v1"));

//app.UseAuthentication();
//app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health/live",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    { Predicate = _ => false });

app.MapHealthChecks("/health/ready",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    { Predicate = h => h.Tags.Contains("ready") });

app.Run();
