using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126170819_PositionIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PositionIndex",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                GameId = table.Column<int>(type: "INTEGER", nullable: false),
                Ply = table.Column<int>(type: "INTEGER", nullable: false),
                Hash = table.Column<long>(type: "INTEGER", nullable: false),
                Uci = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PositionIndex", x => x.Id);
                table.ForeignKey(
                    name: "FK_PositionIndex_Games_GameId",
                    column: x => x.GameId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PositionIndex_GameId_Ply",
            table: "PositionIndex",
            columns: new[] { "GameId", "Ply" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PositionIndex_Hash",
            table: "PositionIndex",
            column: "Hash");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PositionIndex");
    }
}
