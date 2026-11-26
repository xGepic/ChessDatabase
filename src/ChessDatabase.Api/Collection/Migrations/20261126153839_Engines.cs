using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126153839_Engines : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Engines",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Path = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                Arguments = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                Threads = table.Column<int>(type: "INTEGER", nullable: false),
                HashMb = table.Column<int>(type: "INTEGER", nullable: false),
                MultiPv = table.Column<int>(type: "INTEGER", nullable: false),
                ReportedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Engines", x => x.Id);
            });

        migrationBuilder.InsertData(
            table: "Engines",
            columns: new[] { "Id", "Arguments", "HashMb", "MultiPv", "Name", "Path", "ReportedName", "Threads" },
            values: new object[] { 1, null, 8192, 3, "Stockfish", "", null, 16 });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Engines");
    }
}
