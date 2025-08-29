using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using AutoAgentes.Infrastructure;
using AutoAgentes.Infrastructure.Services;
using AutoAgentes.App;
using AutoAgentes.Api;
using AutoAgentes.Contracts;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// SERILOG
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File("logs/autoagentes-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true)
    .CreateLogger();
builder.Host.UseSerilog();

// OpenTelemetry → Azure Monitor
builder.Services.AddOpenTelemetry()
    .UseAzureMonitor(o => o.ConnectionString = builder.Configuration["AzureMonitor:ConnectionString"])
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("AutoAgentes.Orchestrator", "AutoAgentes.McpCaller"))
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation());
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(dispose: true);
builder.Logging.AddOpenTelemetry(o => { o.IncludeScopes = true; o.IncludeFormattedMessage = true; });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AutoAgentes API", Version = "v1" });
});
builder.Services.AddRequestValidation();

// DbContext (SQLite para desarrollo)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration["Postgres:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(cs))
        options.UseSqlite(cs);
});

// CORS - Permitir frontend en puerto 3000
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<ITraceEmitter, SignalRTraceEmitter>();
builder.Services.AddMcpHttpClients();

// Services
builder.Services.AddScoped<IMcpServerRegistry, McpRegistryService>();
builder.Services.AddScoped<IMcpRegistry, McpRegistryService>(); // Alias para compatibilidad
builder.Services.AddScoped<IAgentsService, AgentsService>();
builder.Services.AddScoped<ISessionsService, SessionsService>();
builder.Services.AddScoped<IKernelFactory, KernelFactory>();
builder.Services.AddScoped<IPlanner, Planner>();
builder.Services.AddScoped<IMcpCaller, McpWebSocketCaller>();
builder.Services.AddScoped<IOrchestrator, OrchestratorService>();

// Auth simple por API Key
builder.Services.AddAuthentication("ApiKey")
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SimpleApiKeyAuthHandler>("ApiKey", options => { });

// Rate limiting por API Key
var permitPerMinute = int.TryParse(builder.Configuration["RateLimiting:PermitPerMinute"], out var ppm) ? ppm : 120;
var burst = int.TryParse(builder.Configuration["RateLimiting:Burst"], out var b) ? b : 40;
builder.Services.AddRateLimiter(opts =>
{
    opts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var key = httpContext.Request.Headers["x-api-key"].FirstOrDefault() ?? "anonymous";
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = burst,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            TokensPerPeriod = permitPerMinute,
            AutoReplenishment = true,
            QueueLimit = burst
        });
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();

// Health
app.MapGet("/health/live", () => Results.Ok("ok"));

// SignalR hub
app.MapHub<TraceHub>("/hubs/trace");

// REST endpoints stub
app.MapApi();

app.Run();
