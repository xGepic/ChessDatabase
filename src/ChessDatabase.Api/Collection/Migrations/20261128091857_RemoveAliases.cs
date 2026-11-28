using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261128091857_RemoveAliases : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Aliases",
            table: "Settings");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Aliases",
            table: "Settings",
            type: "TEXT",
            nullable: false,
            defaultValue: "");
    }
}
