using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAndOrganizerRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep existing roles and memberships.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'USER')
                    INSERT INTO [AspNetRoles] ([Name], [NormalizedName], [ConcurrencyStamp])
                    VALUES (N'User', N'USER', CONVERT(nvarchar(36), NEWID()));
                IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'ORGANIZER')
                    INSERT INTO [AspNetRoles] ([Name], [NormalizedName], [ConcurrencyStamp])
                    VALUES (N'Organizer', N'ORGANIZER', CONVERT(nvarchar(36), NEWID()));

                UPDATE [AspNetRoles] SET [Name] = N'User' WHERE [NormalizedName] = N'USER';
                UPDATE [AspNetRoles] SET [Name] = N'Organizer' WHERE [NormalizedName] = N'ORGANIZER';
                DECLARE @UserRoleId int = (SELECT [Id] FROM [AspNetRoles] WHERE [NormalizedName] = N'USER');
                DECLARE @OrganizerRoleId int = (SELECT [Id] FROM [AspNetRoles] WHERE [NormalizedName] = N'ORGANIZER');
                DECLARE @ChangedAccounts TABLE ([UserId] int);

                INSERT INTO [AspNetUserRoles] ([UserId], [RoleId])
                OUTPUT INSERTED.[UserId] INTO @ChangedAccounts
                SELECT u.[Id], @UserRoleId FROM [AspNetUsers] u
                WHERE NOT EXISTS (SELECT 1 FROM [AspNetUserRoles] ur WHERE ur.[UserId] = u.[Id] AND ur.[RoleId] = @UserRoleId);

                INSERT INTO [AspNetUserRoles] ([UserId], [RoleId])
                OUTPUT INSERTED.[UserId] INTO @ChangedAccounts
                SELECT u.[Id], @OrganizerRoleId FROM [AspNetUsers] u
                WHERE EXISTS (SELECT 1 FROM [Events] e WHERE e.[OrganizerUserId] = u.[Id])
                  AND NOT EXISTS (SELECT 1 FROM [AspNetUserRoles] ur WHERE ur.[UserId] = u.[Id] AND ur.[RoleId] = @OrganizerRoleId);

                UPDATE u SET [SecurityStamp] = CONVERT(nvarchar(36), NEWID()),
                             [ConcurrencyStamp] = CONVERT(nvarchar(36), NEWID())
                FROM [AspNetUsers] u WHERE EXISTS (SELECT 1 FROM @ChangedAccounts c WHERE c.[UserId] = u.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Existing memberships may not belong to this migration.
            throw new NotSupportedException("Role backfill cannot be safely reversed automatically. Use a reviewed data migration or restore a backup.");
        }
    }
}
