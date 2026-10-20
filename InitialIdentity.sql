IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [Users] (
    [Id] uniqueidentifier NOT NULL,
    [FirstName] nvarchar(100) NOT NULL,
    [LastName] nvarchar(100) NOT NULL,
    [Email] nvarchar(254) NOT NULL,
    [NormalizedEmail] nvarchar(254) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [PasswordHash] nvarchar(512) NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [IsActive] bit NOT NULL,
    [SessionVersion] int NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);

CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261019000100_InitialIdentity', N'9.0.21');

COMMIT;
GO

