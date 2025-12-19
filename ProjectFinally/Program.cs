using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ProjectFinally.Data;
using ProjectFinally.Data.Seeders;
using ProjectFinally.Helpers;
using ProjectFinally.Repositories.Implementations;
using ProjectFinally.Repositories.Interfaces;
using ProjectFinally.Services.Implementations;
using ProjectFinally.Services.Interfaces;
using Serilog;
using FluentValidation;
using FluentValidation.AspNetCore;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .Build())
    .CreateLogger();

try
{
    Log.Information("Starting YouTube Content API");

var builder = WebApplication.CreateBuilder(args);

// Add Serilog to the logging pipeline
builder.Host.UseSerilog();

// Database Configuration
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// AutoMapper
builder.Services.AddAutoMapper(typeof(Program));

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOAuthService, OAuthService>();
builder.Services.AddScoped<IVideoService, VideoService>();
builder.Services.AddScoped<IYouTubeChannelService, YouTubeChannelService>();
builder.Services.AddScoped<IVideoCategoryService, VideoCategoryService>();
builder.Services.AddScoped<IAdSenseCampaignService, AdSenseCampaignService>();
builder.Services.AddScoped<IAdRevenueService, AdRevenueService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<JwtHelper>();

// HttpClient for OAuth
builder.Services.AddHttpClient();

// Repositories
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IVideoRepository, VideoRepository>();
builder.Services.AddScoped<IAdSenseCampaignRepository, AdSenseCampaignRepository>();
builder.Services.AddScoped<IAdRevenueRepository, AdRevenueRepository>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<ITaskCommentRepository, TaskCommentRepository>();

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// CORS Configuration for React
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173", "http://localhost:5176", "http://localhost:5178", "http://localhost:5179", "http://localhost:5180")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Controllers
builder.Services.AddControllers();

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFluentValidationAutoValidation();

// Swagger/OpenAPI Configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "API de Contenido de YouTube &  Administracion de AdSense ",
        Version = "v1",
        Description = "API for managing YouTube content, AdSense campaigns, employees, and tasks",
        Contact = new OpenApiContact
        {
            Name = "Samuel Soto Trujillo",
            Email = "antonhy1608@gmail.com"
        }
    });

    // JWT Bearer authentication in Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.\n\nExample: 'Bearer eyJhbGciOiJIUzI1NiIs...'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Seed Database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        Log.Information("Starting database seeding");
        var context = services.GetRequiredService<ApplicationDbContext>();
        await DataSeeder.SeedAsync(context);
        Log.Information("Database seeding completed successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "An error occurred while seeding the database");
        throw;
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "YouTube Content API v1");
        c.RoutePrefix = string.Empty; // Swagger at root - si quiero con swagger defrente eliminar esta linea
    });
}

app.UseHttpsRedirection();

app.UseCors("ReactApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

Log.Information("YouTube Content API is running on {Urls}", app.Urls);

app.Run();

Log.Information("YouTube Content API stopped");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
