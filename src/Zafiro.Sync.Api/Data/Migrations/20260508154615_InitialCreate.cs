using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zafiro.Sync.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "apps",
                columns: table => new
                {
                    app_id = table.Column<string>(type: "text", nullable: false),
                    oidc_client_id = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    max_plaintext_bytes = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apps", x => x.app_id);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject = table.Column<string>(type: "text", nullable: false),
                    app_id = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject = table.Column<string>(type: "text", nullable: false),
                    app_id = table.Column<string>(type: "text", nullable: false),
                    file_id = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    change_sequence = table.Column<long>(type: "bigint", nullable: false),
                    encrypted_metadata = table.Column<byte[]>(type: "bytea", nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    cipher_hash = table.Column<string>(type: "text", nullable: false),
                    plaintext_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    ciphertext_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "key_envelopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject = table.Column<string>(type: "text", nullable: false),
                    app_id = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    envelope_version = table.Column<int>(type: "integer", nullable: false),
                    encrypted_app_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_key_envelopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    encrypted_metadata = table.Column<byte[]>(type: "bytea", nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    cipher_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_device_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_file_versions_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_devices_owner_subject_app_id_id",
                table: "devices",
                columns: new[] { "owner_subject", "app_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_versions_file_id_revision",
                table: "file_versions",
                columns: new[] { "file_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_files_owner_subject_app_id_change_sequence",
                table: "files",
                columns: new[] { "owner_subject", "app_id", "change_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_files_owner_subject_app_id_file_id",
                table: "files",
                columns: new[] { "owner_subject", "app_id", "file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_key_envelopes_owner_subject_app_id_device_id_envelope_versi~",
                table: "key_envelopes",
                columns: new[] { "owner_subject", "app_id", "device_id", "envelope_version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apps");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "file_versions");

            migrationBuilder.DropTable(
                name: "key_envelopes");

            migrationBuilder.DropTable(
                name: "files");
        }
    }
}
