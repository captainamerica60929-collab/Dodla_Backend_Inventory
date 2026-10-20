using System.Threading.RateLimiting;
using Inventory.Application;
using Inventory.Infrastructure;
using Inventory.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Inventory API", Version = "v1" });

    // Enable Authorize button in Swagger UI with Bearer token support
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token (without 'Bearer ' prefix)."
    });
    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", null, null),
            new List<string>()
        }
    });
});

// Global Exception Handling & RFC 7807 ProblemDetails
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// CORS: allow only the Angular dev origin (http://localhost:4200)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDevOrigin", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Rate limiter — protect login endpoint: max 10 requests/minute/IP
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = int.TryParse(
            builder.Configuration["RateLimit:LoginPerMinutePerIp"], out var limit) ? limit : 10;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    // Fallback: every endpoint requires authentication unless [AllowAnonymous]
    options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("MastersEdit",  p => p.RequireRole("Admin"));
    options.AddPolicy("MastersView",  p => p.RequireRole("Admin", "StoreKeeper", "Viewer"));
    options.AddPolicy("ApiAdmin",     p => p.RequireRole("Admin"));
    options.AddPolicy("GrnManage",    p => p.RequireRole("Admin", "StoreKeeper"));
    options.AddPolicy("StockOps",     p => p.RequireRole("Admin", "StoreKeeper", "Operator"));
    options.AddPolicy("ReportsView",  p => p.RequireRole("Admin", "StoreKeeper", "Viewer"));
});

// Clean Architecture Layer Dependency Injections
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration, builder.Environment.IsDevelopment());

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AngularDevOrigin");

app.UseRateLimiter();

// IMPORTANT: UseAuthentication must come before UseAuthorization
app.UseAuthentication();
app.UseAuthorization();

// Seed admin user (all environments, idempotent) via scoped SystemCurrentUser override
using (var scope = Inventory.Infrastructure.Services.SystemScopeExtensions.CreateSystemScope(app.Services))
{
    var appUserSeeder = scope.ServiceProvider.GetRequiredService<Inventory.Infrastructure.Services.AppUserSeeder>();
    await appUserSeeder.SeedAsync();
}

// Development-only: seed mock GRN credential via scoped SystemCurrentUser override
if (app.Environment.IsDevelopment())
{
    using var scope = Inventory.Infrastructure.Services.SystemScopeExtensions.CreateSystemScope(app.Services);
    var devSeeder = scope.ServiceProvider.GetRequiredService<Inventory.Infrastructure.Services.DevDataSeeder>();
    await devSeeder.SeedAsync();
}

app.MapControllers();

app.Run();

public partial class Program { }
