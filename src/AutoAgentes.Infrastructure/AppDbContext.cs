using AutoAgentes.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoAgentes.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<McpServer> McpServers => Set<McpServer>();
    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentToolBinding> AgentToolBindings => Set<AgentToolBinding>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<TraceStep> TraceSteps => Set<TraceStep>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<McpServer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.BaseUrl).IsRequired();
            e.Property(x => x.AuthType).IsRequired();
            e.HasMany(x => x.Tools).WithOne(t => t.McpServer).HasForeignKey(t => t.McpServerId);
        });

        modelBuilder.Entity<Tool>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.McpServerId, x.Name }).IsUnique(); // IX_Tool_McpServerId_Name
        });

        modelBuilder.Entity<Agent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique(); // IX_Agent_Name
            e.HasMany(a => a.Bindings).WithOne(b => b.Agent).HasForeignKey(b => b.AgentId);
            e.HasMany(a => a.Sessions).WithOne(s => s.Agent).HasForeignKey(s => s.AgentId);
        });

        modelBuilder.Entity<AgentToolBinding>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AgentId, x.ToolId }).IsUnique(); // IX_AgentToolBinding_AgentId_ToolId
            e.HasOne(x => x.Tool).WithMany(t => t.Bindings).HasForeignKey(x => x.ToolId);
        });

        modelBuilder.Entity<Session>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AgentId, x.StartedUtc }); // IX_Session_AgentId_StartedUtc
            e.HasMany(s => s.Messages).WithOne(m => m.Session).HasForeignKey(m => m.SessionId);
            e.HasMany(s => s.TraceSteps).WithOne(t => t.Session).HasForeignKey(t => t.SessionId);
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<TraceStep>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SessionId, x.Idx }); // IX_TraceStep_SessionId_Idx
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
        });
    }
}


