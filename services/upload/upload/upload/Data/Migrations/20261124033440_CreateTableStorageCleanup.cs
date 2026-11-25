using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Upload.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableStorageCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storage_cleanup",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    video_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    bucket = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    key_or_prefix = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    upload_id = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    reason = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    not_before = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    locked_by = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    locked_until = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    done_at = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_cleanup", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_storage_cleanup_pendentes",
                table: "storage_cleanup",
                columns: new[] { "done_at", "not_before" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_cleanup");
        }
    }
}
