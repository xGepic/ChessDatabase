using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessDatabase.Api.Collection.Migrations;

/// <inheritdoc />
public partial class _20261126121357_GameDetailsTagsEco : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Eco",
            table: "Games",
            type: "TEXT",
            maxLength: 3,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "EventId",
            table: "Games",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Notes",
            table: "Games",
            type: "TEXT",
            maxLength: 10000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OpeningName",
            table: "Games",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Round",
            table: "Games",
            type: "TEXT",
            maxLength: 20,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Site",
            table: "Games",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TimeControl",
            table: "Games",
            type: "TEXT",
            maxLength: 50,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Events",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Tags",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tags", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "GameRecordTagRecord",
            columns: table => new
            {
                GameRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                TagsId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GameRecordTagRecord", x => new { x.GameRecordId, x.TagsId });
                table.ForeignKey(
                    name: "FK_GameRecordTagRecord_Games_GameRecordId",
                    column: x => x.GameRecordId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_GameRecordTagRecord_Tags_TagsId",
                    column: x => x.TagsId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Games_EventId",
            table: "Games",
            column: "EventId");

        migrationBuilder.CreateIndex(
            name: "IX_GameRecordTagRecord_TagsId",
            table: "GameRecordTagRecord",
            column: "TagsId");

        migrationBuilder.AddForeignKey(
            name: "FK_Games_Events_EventId",
            table: "Games",
            column: "EventId",
            principalTable: "Events",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Games_Events_EventId",
            table: "Games");

        migrationBuilder.DropTable(
            name: "Events");

        migrationBuilder.DropTable(
            name: "GameRecordTagRecord");

        migrationBuilder.DropTable(
            name: "Tags");

        migrationBuilder.DropIndex(
            name: "IX_Games_EventId",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "Eco",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "EventId",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "Notes",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "OpeningName",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "Round",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "Site",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "TimeControl",
            table: "Games");
    }
}
