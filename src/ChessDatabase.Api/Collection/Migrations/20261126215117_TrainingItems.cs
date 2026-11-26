using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126215117_TrainingItems : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TrainingItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Repetitions = table.Column<int>(type: "INTEGER", nullable: false),
                IntervalDays = table.Column<int>(type: "INTEGER", nullable: false),
                Ease = table.Column<double>(type: "REAL", nullable: false),
                DueAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TrainingItems", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TrainingItems_Kind_DueAt",
            table: "TrainingItems",
            columns: new[] { "Kind", "DueAt" });

        migrationBuilder.CreateIndex(
            name: "IX_TrainingItems_Kind_Key",
            table: "TrainingItems",
            columns: new[] { "Kind", "Key" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TrainingItems");
    }
}
