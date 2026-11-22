using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorBoilerplate.Modules.Cms.Infrastructure.Migrations.MySql
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CmsLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    RowsJson = table.Column<string>(type: "longtext", nullable: true),
                    CustomCss = table.Column<string>(type: "longtext", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsLayouts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CmsSites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    SiteKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    LogoUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    FaviconUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    DefaultCulture = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    SupportedCulturesJson = table.Column<string>(type: "longtext", nullable: true),
                    ThemeJson = table.Column<string>(type: "longtext", nullable: true),
                    PublishedSnapshotJson = table.Column<string>(type: "longtext", nullable: true),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsSites", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CmsSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LayoutId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Culture = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    Title = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    AnchorSlug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    NavVisible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    BackgroundStyle = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CmsSections_CmsLayouts_LayoutId",
                        column: x => x.LayoutId,
                        principalTable: "CmsLayouts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CmsSections_CmsSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "CmsSites",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CmsLeads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    Email = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    Company = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    ExtraJson = table.Column<string>(type: "longtext", nullable: true),
                    SourceIp = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsLeads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CmsLeads_CmsSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "CmsSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CmsLeads_CmsSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "CmsSites",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CmsWidgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Culture = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    ZoneId = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    WidgetType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: true),
                    PropsJson = table.Column<string>(type: "longtext", nullable: true),
                    StyleJson = table.Column<string>(type: "longtext", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsTemplate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TemplateName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsWidgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CmsWidgets_CmsSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "CmsSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CmsWidgets_CmsSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "CmsSites",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CmsLeads_SectionId",
                table: "CmsLeads",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsLeads_SiteId_CreatedAtUtc",
                table: "CmsLeads",
                columns: new[] { "SiteId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CmsSections_LayoutId",
                table: "CmsSections",
                column: "LayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsSections_SiteId_Culture_SortOrder",
                table: "CmsSections",
                columns: new[] { "SiteId", "Culture", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CmsSites_SiteKey",
                table: "CmsSites",
                column: "SiteKey");

            migrationBuilder.CreateIndex(
                name: "IX_CmsWidgets_SectionId",
                table: "CmsWidgets",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsWidgets_SiteId_Culture_SectionId_SortOrder",
                table: "CmsWidgets",
                columns: new[] { "SiteId", "Culture", "SectionId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CmsLeads");

            migrationBuilder.DropTable(
                name: "CmsWidgets");

            migrationBuilder.DropTable(
                name: "CmsSections");

            migrationBuilder.DropTable(
                name: "CmsLayouts");

            migrationBuilder.DropTable(
                name: "CmsSites");
        }
    }
}
