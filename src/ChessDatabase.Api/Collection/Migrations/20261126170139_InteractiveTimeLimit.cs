using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126170139_InteractiveTimeLimit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "InteractiveSecondsLimit",
            table: "Engines",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.UpdateData(
            table: "Engines",
            keyColumn: "Id",
            keyValue: 1,
            column: "InteractiveSecondsLimit",
            value: null);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "InteractiveSecondsLimit",
            table: "Engines");
    }
}
