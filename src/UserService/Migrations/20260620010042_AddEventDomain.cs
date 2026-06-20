using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kendo.UserService.Migrations
{
    /// <inheritdoc />
    public partial class AddEventDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Headcount = table.Column<int>(type: "integer", nullable: true),
                    DietaryNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SourceText = table.Column<string>(type: "text", nullable: false),
                    IngestionStatus = table.Column<string>(type: "text", nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_events_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_embeddings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: false),
                    EmbeddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_embeddings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_user_embeddings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_embeddings",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: false),
                    EmbeddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_embeddings", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_event_embeddings_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_validations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ok = table.Column<bool>(type: "boolean", nullable: false),
                    ConflictsJson = table.Column<string>(type: "text", nullable: false),
                    SuggestionsJson = table.Column<string>(type: "text", nullable: false),
                    ReasoningTrace = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_validations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_validations_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_embeddings_ModelName",
                table: "event_embeddings",
                column: "ModelName");

            migrationBuilder.CreateIndex(
                name: "IX_event_validations_EventId",
                table: "event_validations",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_CreatedByUserId",
                table: "events",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_embeddings_ModelName",
                table: "user_embeddings",
                column: "ModelName");

            // ── Convert embedding columns to pgvector vector type ────────
            // EF Core maps float[] to real[], but the spec requires vector
            // for pgvector compatibility with the FastAPI service (Phase 05).
            // The ALTER uses the built-in ::vector cast from real[].
            migrationBuilder.Sql("""
                ALTER TABLE event_embeddings
                ALTER COLUMN "Embedding" TYPE vector USING "Embedding"::vector;

                ALTER TABLE user_embeddings
                ALTER COLUMN "Embedding" TYPE vector USING "Embedding"::vector;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_embeddings");

            migrationBuilder.DropTable(
                name: "event_validations");

            migrationBuilder.DropTable(
                name: "user_embeddings");

            migrationBuilder.DropTable(
                name: "events");
        }
    }
}
