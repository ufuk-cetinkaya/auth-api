using System.Text;
using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Middleware;
using AuthService.Models;
using AuthService.Options;
using AuthService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Destructure.ByTransforming<LoginRequest>(r => new { r.Email })
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {TraceId} {Message:lj}{NewLine}{Exception}"));

    builder.Services.AddOptions<JwtOptions>()
        .BindConfiguration("Jwt")
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddDbContext<AppDbContext>(opts =>
        opts.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));

    builder.Services.AddIdentityCore<AppUser>(opts =>
    {
        opts.Password.RequiredLength = 12;
        opts.Password.RequireNonAlphanumeric = true;
        opts.Lockout.MaxFailedAccessAttempts = 5;
        opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

    var jwtOpts = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOpts.Issuer,
                ValidAudience = jwtOpts.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                                               Encoding.UTF8.GetBytes(jwtOpts.SecretKey)),
                ClockSkew = TimeSpan.Zero
            };
            opts.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = ctx =>
                {
                    var logger = ctx.HttpContext.RequestServices
                        .GetRequiredService<ILogger<Program>>();
                    logger.LogWarning("JWT auth failed: {Error}", ctx.Exception.Message);
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("auth-service"))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter());

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("sqlserver", HealthStatus.Unhealthy);

    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    builder.Services.AddScoped<ITokenService, TokenService>();
    builder.Services.AddScoped<IAuthService, AuthService.Services.AuthService>();

    builder.Services.AddOpenApi();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }
    app.UseSerilogRequestLogging(opts =>
    {
        opts.EnrichDiagnosticContext = (diag, ctx) =>
        {
            diag.Set("RequestHost", ctx.Request.Host.Value);
            diag.Set("RequestScheme", ctx.Request.Scheme);
            diag.Set("UserAgent", ctx.Request.Headers.UserAgent.ToString());
        };
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapAuthEndpoints();
    app.MapUserEndpoints();

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = _ => true
    });

    using (var seedScope = app.Services.CreateScope())
    {
        var seederLogger = seedScope.ServiceProvider
            .GetRequiredService<ILogger<Program>>();
        await DbSeeder.SeedAsync(app.Services, seederLogger);
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Auth service başlatılamadı");
}
finally
{
    Log.CloseAndFlush();
}
