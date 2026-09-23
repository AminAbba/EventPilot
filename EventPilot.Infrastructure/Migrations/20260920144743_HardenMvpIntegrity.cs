using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenMvpIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stop if existing data violates the new constraints.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM Events e LEFT JOIN AspNetUsers u ON e.OrganizerUserId = u.Id
                    WHERE u.Id IS NULL OR e.Capacity <= 0 OR e.Price < 0 OR e.EndAt <= e.StartAt
                    OR e.Status NOT IN (0,1,2) OR e.Category NOT BETWEEN 0 AND 17
                    OR DATALENGTH(e.Title) > 200 OR DATALENGTH(e.Location) > 100
                    OR e.Description IS NULL OR DATALENGTH(e.Description) > 2000)
                    THROW 51000, 'Existing event data violates the new integrity rules. Correct it before applying this migration.', 1;
                IF EXISTS (SELECT 1 FROM AspNetUsers WHERE DATALENGTH(FirstName) > 200
                    OR DATALENGTH(LastName) > 200 OR DATALENGTH(PhoneNumber) > 60)
                    THROW 51000, 'Existing profile fields exceed the new limits. Correct them before applying this migration.', 1;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Events",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Location",
                table: "Events",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Events",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Events",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Events_OrganizerUserId",
                table: "Events",
                column: "OrganizerUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Capacity",
                table: "Events",
                sql: "[Capacity] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Category",
                table: "Events",
                sql: "[Category] BETWEEN 0 AND 17");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Dates",
                table: "Events",
                sql: "[EndAt] > [StartAt]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Price",
                table: "Events",
                sql: "[Price] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_Status",
                table: "Events",
                sql: "[Status] IN (0, 1, 2)");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_AspNetUsers_OrganizerUserId",
                table: "Events",
                column: "OrganizerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_AspNetUsers_OrganizerUserId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_OrganizerUserId",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Capacity",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Category",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Dates",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Price",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_Status",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Events");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Events",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Location",
                table: "Events",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Events",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);
        }
    }
}
