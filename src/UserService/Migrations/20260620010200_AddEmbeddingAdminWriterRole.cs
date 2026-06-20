using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kendo.UserService.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingAdminWriterRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'userservice_writer') THEN
                        CREATE ROLE userservice_writer LOGIN PASSWORD 'changeme_userservice_writer';
                    END IF;
                END
                $$;

                GRANT CONNECT ON DATABASE kendo_users TO userservice_writer;
                GRANT USAGE ON SCHEMA public TO userservice_writer;
                GRANT SELECT, INSERT, UPDATE ON event_embeddings TO userservice_writer;
                GRANT SELECT, INSERT, UPDATE ON events, event_validations TO userservice_writer;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM userservice_writer;
                REVOKE USAGE ON SCHEMA public FROM userservice_writer;
                REVOKE CONNECT ON DATABASE kendo_users FROM userservice_writer;
                DROP ROLE IF EXISTS userservice_writer;
                """);
        }
    }
}
