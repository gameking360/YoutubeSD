using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upload.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableVideoUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "video_upload",
                columns: table => new
                {
                    video_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    owner_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    visibility = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    duration_sec = table.Column<int>(type: "int", nullable: true),
                    hls_bucket = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    master_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    renditions = table.Column<string>(type: "json", nullable: true),
                    thumbnail_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    published_at = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    removed_at = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    extension = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    content_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    chunk_size = table.Column<int>(type: "int", nullable: false),
                    total_parts = table.Column<int>(type: "int", nullable: false),
                    raw_bucket = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    raw_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    s3_upload_id = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    upload_state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    quota_state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    processing_state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    processing_attempts = table.Column<int>(type: "int", nullable: false),
                    failure_reason = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    queue_deadline_at = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    deadline_at = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    last_activity_at = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_video_upload", x => x.video_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_video_upload_dono",
                table: "video_upload",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_video_upload_prazos",
                table: "video_upload",
                columns: new[] { "status", "processing_state", "deadline_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "video_upload");
        }
    }
}
