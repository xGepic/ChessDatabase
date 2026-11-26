using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126154910_GameAnalysis : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AnalysisMsPerMove",
            table: "Engines",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "EngineAssessments",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                GameId = table.Column<int>(type: "INTEGER", nullable: false),
                EngineName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Threads = table.Column<int>(type: "INTEGER", nullable: false),
                HashMb = table.Column<int>(type: "INTEGER", nullable: false),
                MsPerMove = table.Column<int>(type: "INTEGER", nullable: false),
                AnalysedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                MainLine = table.Column<string>(type: "TEXT", nullable: false),
                Positions = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EngineAssessments", x => x.Id);
                table.ForeignKey(
                    name: "FK_EngineAssessments_Games_GameId",
                    column: x => x.GameId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.UpdateData(
            table: "Engines",
            keyColumn: "Id",
            keyValue: 1,
            column: "AnalysisMsPerMove",
            value: 2000);

        migrationBuilder.CreateIndex(
            name: "IX_EngineAssessments_GameId",
            table: "EngineAssessments",
            column: "GameId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EngineAssessments");

        migrationBuilder.DropColumn(
            name: "AnalysisMsPerMove",
            table: "Engines");
    }
}
