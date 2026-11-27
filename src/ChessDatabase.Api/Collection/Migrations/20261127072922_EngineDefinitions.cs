using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261127072922_EngineDefinitions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AnalysisEngineId",
            table: "Settings",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "InteractiveEngineId",
            table: "Settings",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Options",
            table: "Engines",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<int>(
            name: "EngineId",
            table: "EngineAssessments",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.UpdateData(
            table: "Engines",
            keyColumn: "Id",
            keyValue: 1,
            column: "Options",
            value: "[]");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AnalysisEngineId",
            table: "Settings");

        migrationBuilder.DropColumn(
            name: "InteractiveEngineId",
            table: "Settings");

        migrationBuilder.DropColumn(
            name: "Options",
            table: "Engines");

        migrationBuilder.DropColumn(
            name: "EngineId",
            table: "EngineAssessments");
    }
}
