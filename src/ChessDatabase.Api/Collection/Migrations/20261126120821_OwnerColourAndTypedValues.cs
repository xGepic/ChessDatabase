using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126120821_OwnerColourAndTypedValues : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "OwnerColor",
            table: "Games",
            newName: "OwnerColour");

        // NAGs move from space-separated text ("1 14") to a JSON array ("[1,14]").
        migrationBuilder.Sql("UPDATE MoveNodes SET Nags = '[' || replace(trim(Nags), ' ', ',') || ']';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE MoveNodes SET Nags = replace(replace(replace(Nags, '[', ''), ']', ''), ',', ' ');");

        migrationBuilder.RenameColumn(
            name: "OwnerColour",
            table: "Games",
            newName: "OwnerColor");
    }
}
