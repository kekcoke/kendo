using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kendo.Worker.Migrations
{
    /// <inheritdoc />
    public partial class AddDlqRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DlqRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalMessageType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeadLetterReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeadLetterErrorDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeliveryCount = table.Column<int>(type: "integer", nullable: false),
                    EnqueuedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Alerted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DlqRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DlqRecords_DetectedAt",
                table: "DlqRecords",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DlqRecords_OriginalMessageId",
                table: "DlqRecords",
                column: "OriginalMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DlqRecords");
        }
    }
}
