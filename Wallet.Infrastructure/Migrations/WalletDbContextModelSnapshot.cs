using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
namespace Wallet.Infrastructure.Migrations;
[DbContext(typeof(WalletDbContext))]
public sealed class WalletDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "9.0.21");
        modelBuilder.Entity("Wallet.Domain.User", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uniqueidentifier");
            b.Property<string>("FirstName").IsRequired().HasMaxLength(100).HasColumnType("nvarchar(100)");
            b.Property<string>("LastName").IsRequired().HasMaxLength(100).HasColumnType("nvarchar(100)");
            b.Property<string>("Email").IsRequired().HasMaxLength(254).HasColumnType("nvarchar(254)");
            b.Property<string>("NormalizedEmail").IsRequired().HasMaxLength(254).HasColumnType("nvarchar(254)").UseCollation("Latin1_General_100_BIN2");
            b.Property<string>("PasswordHash").IsRequired().HasMaxLength(512).HasColumnType("nvarchar(512)");
            b.Property<string>("Role").IsRequired().HasMaxLength(20).HasColumnType("nvarchar(20)");
            b.Property<bool>("IsActive").HasColumnType("bit");
            b.Property<int>("SessionVersion").HasColumnType("int");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("datetimeoffset");
            b.Property<byte[]>("RowVersion").IsRequired().IsConcurrencyToken().ValueGeneratedOnAddOrUpdate().HasColumnType("rowversion");
            b.HasKey("Id");
            b.HasIndex("NormalizedEmail").IsUnique();
            b.ToTable("Users");
        });

    }
}
