using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126182402_LichessTokenOutOfCollection : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LichessToken",
            table: "Settings");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LichessToken",
            table: "Settings",
            type: "TEXT",
            nullable: true);
    }
}
