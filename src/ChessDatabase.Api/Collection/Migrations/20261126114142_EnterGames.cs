using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126114142_EnterGames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "BlackElo",
            table: "Games",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "CreatedAt",
            table: "Games",
            type: "TEXT",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.AddColumn<string>(
            name: "Date",
            table: "Games",
            type: "TEXT",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "OpponentId",
            table: "Games",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "OwnerColor",
            table: "Games",
            type: "TEXT",
            maxLength: 5,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Result",
            table: "Games",
            type: "TEXT",
            maxLength: 7,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "WhiteElo",
            table: "Games",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MoveNodes",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                GameId = table.Column<int>(type: "INTEGER", nullable: false),
                ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                Order = table.Column<int>(type: "INTEGER", nullable: false),
                Uci = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                San = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                CommentBefore = table.Column<string>(type: "TEXT", nullable: true),
                CommentAfter = table.Column<string>(type: "TEXT", nullable: true),
                Nags = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MoveNodes", x => x.Id);
                table.ForeignKey(
                    name: "FK_MoveNodes_Games_GameId",
                    column: x => x.GameId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MoveNodes_MoveNodes_ParentId",
                    column: x => x.ParentId,
                    principalTable: "MoveNodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Players",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Players", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Settings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OwnerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Settings", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Games_OpponentId",
            table: "Games",
            column: "OpponentId");

        migrationBuilder.CreateIndex(
            name: "IX_MoveNodes_GameId_ParentId_Order",
            table: "MoveNodes",
            columns: new[] { "GameId", "ParentId", "Order" });

        migrationBuilder.CreateIndex(
            name: "IX_MoveNodes_ParentId",
            table: "MoveNodes",
            column: "ParentId");

        migrationBuilder.AddForeignKey(
            name: "FK_Games_Players_OpponentId",
            table: "Games",
            column: "OpponentId",
            principalTable: "Players",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Games_Players_OpponentId",
            table: "Games");

        migrationBuilder.DropTable(
            name: "MoveNodes");

        migrationBuilder.DropTable(
            name: "Players");

        migrationBuilder.DropTable(
            name: "Settings");

        migrationBuilder.DropIndex(
            name: "IX_Games_OpponentId",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "BlackElo",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "CreatedAt",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "Date",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "OpponentId",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "OwnerColor",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "Result",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "WhiteElo",
            table: "Games");
    }
}
