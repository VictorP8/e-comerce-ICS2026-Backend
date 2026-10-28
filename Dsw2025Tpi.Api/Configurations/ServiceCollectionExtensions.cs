using Dsw2025Tpi.Api.NewFolder;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Data.Repositories;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json.Serialization;

namespace Dsw2025Tpi.Api.Configuration
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDomainServices(this IServiceCollection services, IConfiguration configuration)
        {
            // 1. Controladores y JSON
            services.AddControllers().AddJsonOptions(opt =>
            {
                opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

            // 2. Swagger y JWT
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(o =>
            {
                o.SwaggerDoc("v1", new OpenApiInfo { Title = "Desarrollo de software TPI", Version = "v1" });
                o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    In = ParameterLocation.Header,
                    Name = "Authorization",
                    Description = "Ingrese el token",
                    Type = SecuritySchemeType.ApiKey
                });
                o.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            // Base de Datos (Entity Framework)
            var connectionString = configuration.GetConnectionString("Dsw2025TpiEntities");
            services.AddDbContext<AuthenticateContext>(options => options.UseSqlServer(connectionString));
            services.AddDbContext<Dsw2025TpiContext>(options => options.UseSqlServer(connectionString));

            // Identity
            services.AddIdentity<IdentityUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = 8;
            })
            .AddEntityFrameworkStores<AuthenticateContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>();

            // Autenticación JWT
            var jwtConfig = configuration.GetSection("Jwt");
            var keyText = jwtConfig["Key"] ?? throw new ArgumentNullException("JWT Key");
            var key = Encoding.UTF8.GetBytes(keyText);

            services.AddAuthentication(options =>
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
                    ValidIssuer = jwtConfig["issuer"],
                    ValidAudience = jwtConfig["audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                };
            });
            services.AddAuthorization();

            // Inyección de Dependencias (DI)
            services.AddSingleton<JwtTokenService>();
            services.AddScoped<IRepository, EfRepository>();
            services.AddScoped<ProductsManagementService>();
            services.AddScoped<OrdersManagementService>();

            // CORS y HealthCheck
            services.AddHealthChecks();
            services.AddCors(options =>
            {
                options.AddPolicy("AllowFrontend", policy =>
                {
                    policy.WithOrigins("http://localhost:5173")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            return services;
        }
    }
}
