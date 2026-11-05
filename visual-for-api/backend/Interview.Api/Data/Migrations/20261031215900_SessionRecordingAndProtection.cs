using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interview.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SessionRecordingAndProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Room_Status",
                table: "Interviews");

            migrationBuilder.AlterColumn<string>(
                name: "Input",
                table: "TaskTests",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000);

            migrationBuilder.AlterColumn<string>(
                name: "Expected",
                table: "TaskTests",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000);

            migrationBuilder.AlterColumn<string>(
                name: "StarterCode",
                table: "Tasks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20000)",
                oldMaxLength: 20000);

            migrationBuilder.AddColumn<decimal>(
                name: "Weight",
                table: "RubricCriteria",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AlterColumn<string>(
                name: "TaskSnapshot",
                table: "InterviewTasks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "RubricSnapshot",
                table: "Interviews",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Interviews",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<int>(
                name: "NextEventSequence",
                table: "Interviews",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SessionExpiresAt",
                table: "Interviews",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "Hints",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "Evaluations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(5000)",
                oldMaxLength: 5000);

            migrationBuilder.AddColumn<string>(
                name: "ErrorKind",
                table: "CodeRunTests",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "CodeEvents",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20000)",
                oldMaxLength: 20000);

            migrationBuilder.AddColumn<int>(
                name: "SessionSequence",
                table: "CodeEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "CodeDocuments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20000)",
                oldMaxLength: 20000);

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "ChatMessages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.Sql("UPDATE \"Interviews\" SET \"SessionExpiresAt\" = COALESCE(\"StartedAt\", \"CreatedAt\") + \"DurationMinutes\" * INTERVAL '1 minute' WHERE \"Status\" = 'active' AND \"SessionExpiresAt\" IS NULL");
            migrationBuilder.CreateTable(
                name: "SessionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    Author = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionEvents_Interviews_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Interviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Criterion_Weight",
                table: "RubricCriteria",
                sql: "\"Weight\" > 0 AND \"Weight\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Room_Duration",
                table: "Interviews",
                sql: "\"DurationMinutes\" BETWEEN 5 AND 240");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Room_Status",
                table: "Interviews",
                sql: "\"Status\" IN ('draft','waiting','active','completed')");

            migrationBuilder.CreateIndex(
                name: "IX_SessionEvents_ActorId",
                table: "SessionEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionEvents_RoomId_Sequence",
                table: "SessionEvents",
                columns: new[] { "RoomId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Criterion_Weight",
                table: "RubricCriteria");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Room_Duration",
                table: "Interviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Room_Status",
                table: "Interviews");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "RubricCriteria");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Interviews");

            migrationBuilder.DropColumn(
                name: "NextEventSequence",
                table: "Interviews");

            migrationBuilder.DropColumn(
                name: "SessionExpiresAt",
                table: "Interviews");

            migrationBuilder.DropColumn(
                name: "ErrorKind",
                table: "CodeRunTests");

            migrationBuilder.DropColumn(
                name: "SessionSequence",
                table: "CodeEvents");

            migrationBuilder.AlterColumn<string>(
                name: "Input",
                table: "TaskTests",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Expected",
                table: "TaskTests",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "StarterCode",
                table: "Tasks",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TaskSnapshot",
                table: "InterviewTasks",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RubricSnapshot",
                table: "Interviews",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "Hints",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "Evaluations",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "CodeEvents",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "CodeDocuments",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "ChatMessages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Room_Status",
                table: "Interviews",
                sql: "\"Status\" IN ('draft','active','completed')");
        }
    }
}
