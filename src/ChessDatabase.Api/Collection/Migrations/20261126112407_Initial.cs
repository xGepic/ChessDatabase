using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126112407_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Games",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Games", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Games");
    }
}
