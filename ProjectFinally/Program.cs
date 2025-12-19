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
using Microsoft.Extensions.FileProviders;
using System.IO;

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

    // Add Serilog
    builder.Host.UseSerilog();

    // Database
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

    // HttpClient
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

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("ReactApp", policy =>
        {
            policy.WithOrigins(
                "http://localhost:3000",
                "http://localhost:5173",
                "http://localhost:5176",
                "http://localhost:5178",
                "http://localhost:5179",
                "http://localhost:5180",
                "https://your-frontend.onrender.com" // <-- Cambiar por tu frontend en Render
            )
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

    // Swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "API de Contenido de YouTube & Administración de AdSense",
            Version = "v1",
            Description = "API for managing YouTube content, AdSense campaigns, employees, and tasks",
            Contact = new OpenApiContact
            {
                Name = "Samuel Soto Trujillo",
                Email = "antonhy1608@gmail.com"
            }
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer eyJhbGciOiJIUzI1NiIs...'",
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

    // Seed Database (no detiene app si falla)
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
            Log.Warning(ex, "Database seeding failed, continuing startup.");
        }
    }

    // Middleware
    // app.UseHttpsRedirection(); // Comentado temporalmente por Render

    app.UseCors("ReactApp");

    app.UseAuthentication();
    app.UseAuthorization();

    // Swagger disponible en producción
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "YouTube Content API v1");
        c.RoutePrefix = string.Empty; // Swagger en la raíz
    });

    // Servir frontend React si existe
    var frontendPath = Path.Combine(Directory.GetCurrentDirectory(), "build");
    if (Directory.Exists(frontendPath))
    {
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(frontendPath)
        });

        app.MapFallbackToFile("index.html");
    }

    // Map controllers
    app.MapControllers();

    // Obtener puerto de Render
    var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
    Log.Information("YouTube Content API is running on port {Port}", port);
    app.Run($"http://0.0.0.0:{port}");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
