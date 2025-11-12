using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;

// Фабрика для створення контексту під час міграцій
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {

        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cs = configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException("Connection string 'Default' is not configured. Provide it via user secrets, environment variables, or appsettings.");
        }

        var serverVersion = new MySqlServerVersion(new Version(8, 0, 0)); 

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(cs, serverVersion)
            .Options;

        return new AppDbContext(options);
    }
}
