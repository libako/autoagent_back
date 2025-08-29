using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AutoAgentes.Infrastructure;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        // Usa variable de entorno o valor por defecto local
        var cs = Environment.GetEnvironmentVariable("AUTOAGENTES_DB")
                 ?? "Data Source=autoagentes.db";
        optionsBuilder.UseSqlite(cs);
        return new AppDbContext(optionsBuilder.Options);
    }
}


