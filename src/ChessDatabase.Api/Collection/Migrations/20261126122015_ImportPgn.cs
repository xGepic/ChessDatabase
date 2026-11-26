using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126122015_ImportPgn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Aliases",
            table: "Settings",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.CreateTable(
            name: "ImportReviewItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Pgn = table.Column<string>(type: "TEXT", nullable: false),
                Reason = table.Column<string>(type: "TEXT", nullable: false),
                White = table.Column<string>(type: "TEXT", nullable: true),
                Black = table.Column<string>(type: "TEXT", nullable: true),
                Date = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportReviewItems", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ImportReviewItems");

        migrationBuilder.DropColumn(
            name: "Aliases",
            table: "Settings");
    }
}
