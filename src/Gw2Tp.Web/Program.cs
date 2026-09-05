using Gw2Tp.Web.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Routing;

namespace Gw2Tp.Web;

public static class Program
{
    public static void Main(string[] args)
    {
        CreateApplication(args).Run();
    }

    internal static WebApplication CreateApplication(
        string[] args,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureBuilder?.Invoke(builder);

        var hostOptions = LocalHostOptions.FromConfiguration(builder.Configuration);
        LocalHostOptionsValidator.ValidateAndThrow(hostOptions, builder.Configuration);

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            // Replace WebApplicationBuilder's reloadable Kestrel configuration loader.
            // Listener authority belongs exclusively to the validated options below.
            serverOptions.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);

            foreach (var address in hostOptions.GetListenAddresses())
            {
                serverOptions.Listen(address, hostOptions.Port);
            }
        });

        builder.Services.AddHealthChecks();
        builder.Services.AddHostFiltering(options =>
        {
            options.AllowedHosts = hostOptions.AllowedHosts;
        });

        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(LocalHostOptions.DevelopmentCorsPolicy, policy =>
                {
                    policy
                        .WithOrigins(hostOptions.TrustedDevelopmentOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });
        }

        builder.Services.AddSingleton(hostOptions);
        builder.Services.AddSingleton<LocalRequestOriginValidator>();

        var app = builder.Build();

        app.UseHostFiltering();
        if (app.Environment.IsDevelopment())
        {
            app.UseCors(LocalHostOptions.DevelopmentCorsPolicy);
        }

        app.UseMiddleware<LocalRequestOriginProtectionMiddleware>();

        app.MapHealthChecks(
                "/api/health",
                new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync })
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));
        app.Map("/api/{**path}", () => Results.NotFound(new { error = "api_route_not_found" }));

        MapFrontend(app, builder.Configuration);

        return app;
    }

    private static void MapFrontend(WebApplication app, IConfiguration configuration)
    {
        if (app.Environment.IsDevelopment())
        {
            return;
        }

        var frontendPath = FrontendPathResolver.Resolve(app.Environment.ContentRootPath, configuration);
        if (frontendPath is null)
        {
            return;
        }

        var fileProvider = new PhysicalFileProvider(frontendPath);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fileProvider });
    }
}
