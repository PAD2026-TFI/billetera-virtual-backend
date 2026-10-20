using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Wallet.Infrastructure.Migrations;
[DbContext(typeof(WalletDbContext))]
[Migration("20261019000100_InitialIdentity")]
public sealed class InitialIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("Users", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
            NormalizedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false, collation: "Latin1_General_100_BIN2"),
            PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
            Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
            IsActive = table.Column<bool>(type: "bit", nullable: false),
            SessionVersion = table.Column<int>(type: "int", nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Users", x => x.Id));
        migrationBuilder.CreateIndex("IX_Users_NormalizedEmail", "Users", "NormalizedEmail", unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Users");
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
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
