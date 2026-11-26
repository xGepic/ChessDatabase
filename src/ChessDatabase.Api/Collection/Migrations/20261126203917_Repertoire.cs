using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126203917_Repertoire : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RepertoireNodes",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Side = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                Order = table.Column<int>(type: "INTEGER", nullable: false),
                Uci = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                San = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                Comment = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RepertoireNodes", x => x.Id);
                table.ForeignKey(
                    name: "FK_RepertoireNodes_RepertoireNodes_ParentId",
                    column: x => x.ParentId,
                    principalTable: "RepertoireNodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RepertoireNodes_ParentId",
            table: "RepertoireNodes",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "IX_RepertoireNodes_Side_ParentId_Order",
            table: "RepertoireNodes",
            columns: new[] { "Side", "ParentId", "Order" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RepertoireNodes");
    }
}
