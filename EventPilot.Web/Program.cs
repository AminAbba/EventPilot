using EventPilot.Application;
using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Abstractions.Email;
using EventPilot.Application.Common.Behaviors;
using EventPilot.Application.Events.Commands.CreateEvent;
using EventPilot.Application.Events.Interfaces;
using EventPilot.Application.Registrations.Interfaces;
using EventPilot.Infrastructure.Auth;
using EventPilot.Infrastructure.Email;
using EventPilot.Infrastructure.Identity;
using EventPilot.Infrastructure.Persistance;
using EventPilot.Infrastructure.Persistance.Repositories;
using EventPilot.Infrastructure.Persistence;
using EventPilot.Web.Auth;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using EventPilot.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using EventPilot.Web.Operations;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog((services, logger) => EventPilot.Web.LoggingConfiguration.Configure(logger, services, builder.Configuration));
builder.Services.AddRateLimiter(EventPilot.Web.AuthenticationRateLimits.Configure);
builder.AddOperationalServices();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
var appBaseUrl = builder.Configuration["App:BaseUrl"];
if (!Uri.TryCreate(appBaseUrl, UriKind.Absolute, out var appBaseUri)
    || (appBaseUri.Scheme != Uri.UriSchemeHttp && appBaseUri.Scheme != Uri.UriSchemeHttps)
    || !string.IsNullOrEmpty(appBaseUri.Query)
    || !string.IsNullOrEmpty(appBaseUri.Fragment)
    || !string.IsNullOrEmpty(appBaseUri.UserInfo))
{
    throw new InvalidOperationException("App:BaseUrl must be an absolute HTTP(S) URL without credentials, a query, or a fragment.");
}

appBaseUri = new Uri(appBaseUri.AbsoluteUri.TrimEnd('/') + "/");
builder.Services.Configure<PasswordResetOptions>(options =>
    options.ResetLinkBase = appBaseUri.AbsoluteUri);

builder.Services.AddDbContextFactory<EventPilotDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole<int>>()
    .AddSignInManager()
    .AddEntityFrameworkStores<EventPilotDbContext>()
    .AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));

// Seed demo data and exit.
if (args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Demo data can only be seeded in Development.");

    builder.Services.AddAuthentication();
    builder.Services.AddDataProtection();
    await using var seedApp = builder.Build();
    await using var scope = seedApp.Services.CreateAsyncScope();
    await DemoDataSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<EventPilotDbContext>(),
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    return;
}

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt options are missing.");

if (string.IsNullOrWhiteSpace(jwt.Key) || Encoding.UTF8.GetByteCount(jwt.Key) < 32)
{
    throw new InvalidOperationException(
        "Configure Jwt:Key using User Secrets or the Jwt__Key environment variable with a random secret of at least 32 bytes.");
}
if (string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience) || jwt.ExpireMinutes is < 1 or > 1440)
    throw new InvalidOperationException("JWT issuer/audience and an expiry between 1 and 1440 minutes are required.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = JwtAccountValidation.Validate,
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await context.Response.WriteAsJsonAsync(ApiResult.Failure("Authentication required."));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(ApiResult.Failure("Access denied."));
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy(AuthorizationPolicies.CanManageEvents, policy =>
        policy.RequireAuthenticatedUser().RequireRole(AppRoles.Organizer)));

builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<IRegistrationRepository, RegistrationRepository>();
builder.Services.AddScoped<IRegistrationSeatStore, RegistrationSeatStore>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();

builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OperationLoggingBehavior<,>));

builder.Services.AddValidatorsFromAssemblyContaining<CreateEventCommandValidator>();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(ApplicationAssemblyMarker).Assembly);
});

builder.Services.AddHttpClient("LocalApi", client =>
{
    client.BaseAddress = appBaseUri;
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    // Allow local certificates in Development.
    if (builder.Environment.IsDevelopment())
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
    }

    return new HttpClientHandler();
});

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new EventPilot.Web.Serialization.UtcDateTimeConverter()))
    .ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
        ApiResult.Failure("Validation failed.", context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .Select(x => $"{x.Key}: Invalid or missing value.").ToList()));
});
builder.Services.AddExceptionHandler<EventPilot.Web.ApiExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "EventPilot API",
        Version = "v1",
        Description = "EventPilot Backend API"
    });

    options.AddSecurityDefinition("Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter JWT Token"
        });

    options.OperationFilter<EventPilot.Web.SwaggerAuthorizationFilter>();
});


builder.Services.AddRazorPages();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseSerilogRequestLogging(EventPilot.Web.LoggingConfiguration.ConfigureRequestLogging);
app.UseExceptionHandler("/Error");
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
        await response.WriteAsJsonAsync(ApiResult.Failure(response.StatusCode switch
        {
            404 => "Resource not found.",
            405 => "HTTP method not allowed.",
            415 => "Unsupported media type.",
            _ => "Request failed."
        }));
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSwagger();

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint(
        "/swagger/v1/swagger.json",
        "EventPilot API v1");

    options.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapOperationalEndpoints();
app.MapRazorPages();

app.Run();

public partial class Program { }
