using Microsoft.EntityFrameworkCore;
using Middt.UBKS.WebApi.config.Helper;
using Middt.UBKS.WebApi.config.Model;

#nullable disable

namespace Middt.Template.Api.Model.Database
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

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                //#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see http://go.microsoft.com/fwlink/?LinkId=723263.
                optionsBuilder.UseSqlServer(new ConfigurationHelper().Get<DBSettings>().TestDB);
            }
        }

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
