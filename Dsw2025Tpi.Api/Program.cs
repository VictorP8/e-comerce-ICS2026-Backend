using Dsw2025Tpi.Api.Configuration;
using Dsw2025Tpi.Api.NewFolder;

var builder = WebApplication.CreateBuilder(args);

// 1. Configuración de Logs
builder.Services.AddLogging(config =>
{
    config.ClearProviders();
    config.AddConsole();
    var path = builder.Configuration.GetValue<string>("LogPath");
    if (!string.IsNullOrEmpty(path)) config.AddFile(path);
});

// 2. Inyección de todos los servicios del dominio (Entity, JWT, Swagger, etc.)
builder.Services.AddDomainServices(builder.Configuration);

var app = builder.Build();

// 3. Ejecutar Seeding de Datos (Creación de roles y carga inicial)
await app.SeedDatabaseAsync();

// 4. Configurar el pipeline HTTP (Middlewares)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/healthcheck");

app.Run();