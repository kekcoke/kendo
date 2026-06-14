using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kendo.UserService.Migrations
{
    /// <inheritdoc />
    public partial class AddTraceContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceContext",
                table: "OutboxMessages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceContext",
                table: "OutboxMessages");
        }
    }
}
