using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upload.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableUploadPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "upload_part",
                columns: table => new
                {
                    video_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    part_number = table.Column<int>(type: "int", nullable: false),
                    etag = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "datetime(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_part", x => new { x.video_id, x.part_number });
                    table.ForeignKey(
                        name: "fk_upload_part_video_upload_video_id",
                        column: x => x.video_id,
                        principalTable: "video_upload",
                        principalColumn: "video_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "upload_part");
        }
    }
}
