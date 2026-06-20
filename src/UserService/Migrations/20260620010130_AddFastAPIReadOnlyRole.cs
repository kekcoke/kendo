using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kendo.UserService.Migrations
{
    /// <inheritdoc />
    public partial class AddFastAPIReadOnlyRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fastapi_ro') THEN
                        CREATE ROLE fastapi_ro LOGIN PASSWORD 'changeme_fastapi_ro';
                    END IF;
                END
                $$;

                GRANT CONNECT ON DATABASE kendo_users TO fastapi_ro;
                GRANT USAGE ON SCHEMA public TO fastapi_ro;
                GRANT SELECT ON event_embeddings, user_embeddings, events, event_validations TO fastapi_ro;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO fastapi_ro;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM fastapi_ro;
                REVOKE USAGE ON SCHEMA public FROM fastapi_ro;
                REVOKE CONNECT ON DATABASE kendo_users FROM fastapi_ro;
                DROP ROLE IF EXISTS fastapi_ro;
                """);
        }
    }
}
