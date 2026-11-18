using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorBoilerplate.Modules.Localization.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialLocalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocLanguages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Culture = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FlagCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    IsRtl = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocLanguages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModuleId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Culture = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    IsOverridden = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocResources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocLanguages_Culture",
                table: "LocLanguages",
                column: "Culture",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocResources_Culture_Key",
                table: "LocResources",
                columns: new[] { "Culture", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_LocResources_ModuleId_Culture_Key",
                table: "LocResources",
                columns: new[] { "ModuleId", "Culture", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocLanguages");

            migrationBuilder.DropTable(
                name: "LocResources");
        }
    }
}
