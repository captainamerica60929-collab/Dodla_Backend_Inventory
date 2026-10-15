using Inventory.Application;
using Inventory.Infrastructure;
using Inventory.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

    // Idempotent Development seed for Mock GRN API credential
    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<Inventory.Infrastructure.Services.DevDataSeeder>();
        await seeder.SeedAsync();
    }
}

app.UseHttpsRedirection();

app.UseCors("AngularDevOrigin");

app.UseAuthorization();

app.MapControllers();

app.Run();
