using System;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Adp.Persistent;

public sealed class YnabDbContext : DbContext
{
    public YnabDbContext( IConfiguration configuration )
    {
        this.configuration = configuration;
        Database.EnsureCreated();
    }

    private readonly IConfiguration configuration;

    public DbSet< User > Users { get; set; }

    protected override void OnConfiguring( DbContextOptionsBuilder optionsBuilder )
    {
        var database = configuration[ "MYSQL_DATABASE" ];
        var user = configuration[ "MYSQL_USER" ];
        var password = configuration[ "MYSQL_PASSWORD" ];

        optionsBuilder.UseMySql( $"server=host.docker.internal;UserId={user};Password={password};database={database};", new MySqlServerVersion( new Version( 8, 0, 22 ) ) );
    }

    protected override void OnModelCreating( ModelBuilder modelBuilder )
    {
        modelBuilder.Entity< User >().HasMany( static item => item.BankAccountToYnabAccounts ).WithOne();
        base.OnModelCreating( modelBuilder );
    }
}