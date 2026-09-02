using Entities;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders; // Add this using directive
namespace EntitiesStocks
{



    public class StocksDbContext : DbContext
    {
        public StocksDbContext(DbContextOptions<StocksDbContext> options) : base(options)
        {
        }

        public DbSet<BuyOrder> BuyOrders { get; set; }
        public DbSet<SellOrder> SellOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<BuyOrder>()
                .ToTable("BuyOrders")
                .HasKey(b => b.BuyOrderID);

            modelBuilder.Entity<SellOrder>()
                .ToTable("SellOrders")
                .HasKey(s => s.SellOrderID);
        }
    }
}