using Microsoft.EntityFrameworkCore;
using Middt.Sample.Api.config.Helper;
using Middt.Sample.Api.config.Model;

#nullable disable

namespace Middt.Sample.Api.Model.Database
{
    public partial class Frameworkv2Context : DbContext
    {
        public Frameworkv2Context()
        {
        }

        public Frameworkv2Context(DbContextOptions<Frameworkv2Context> options)
            : base(options)
        {
        }

        public virtual DbSet<Table1> Table1s { get; set; }
        public virtual DbSet<TableHistory> TableHistories { get; set; }



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("Relational:Collation", "Turkish_CI_AS");

            modelBuilder.Entity<Table1>(entity =>
            {
                entity.ToTable("Table1");

                entity.Property(e => e.Table1Id).HasColumnName("Table1ID");

                entity.Property(e => e.Ad)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.Tarih).HasColumnType("datetime");
            });

            modelBuilder.Entity<TableHistory>(entity =>
            {
                entity.ToTable("TableHistory");

                entity.Property(e => e.Id).HasColumnName("ID");

                entity.Property(e => e.Ad)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.EklenmeTarihi).HasColumnType("datetime");

                entity.Property(e => e.ValidFrom).HasDefaultValueSql("(getutcdate())");

                entity.Property(e => e.ValidTill).HasDefaultValueSql("(CONVERT([datetime2],'12/31/9999 23:59:59.9999999'))");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
