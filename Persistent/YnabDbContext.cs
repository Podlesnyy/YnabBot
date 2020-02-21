﻿using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Persistent
{
    public sealed class YnabDbContext : DbContext
    {
        private readonly IConfiguration configuration;
        public DbSet<User> Users { get; set; }

        public YnabDbContext(IConfiguration configuration)
        {
            this.configuration = configuration;
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var database = configuration.GetValue<string>("MYSQL_NEW_DATABASE");
            var user = configuration.GetValue<string>("MYSQL_USER");
            var password = configuration.GetValue<string>("MYSQL_PASSWORD");

            optionsBuilder.UseMySql($"server=127.0.0.1;UserId={user};Password={password};database={database};");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasMany(item => item.BankAccountToYnabAccounts).WithOne();
            base.OnModelCreating(modelBuilder);
        }
    }
}