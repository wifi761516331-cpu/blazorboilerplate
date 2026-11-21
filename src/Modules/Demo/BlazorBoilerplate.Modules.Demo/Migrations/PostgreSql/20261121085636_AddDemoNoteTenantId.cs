using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorBoilerplate.Modules.Demo.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddDemoNoteTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DemoNotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemoNotes_TenantId",
                table: "DemoNotes",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DemoNotes_TenantId",
                table: "DemoNotes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DemoNotes");
        }
    }
}
