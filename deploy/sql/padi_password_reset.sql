BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111120034_AddPasswordReset'
)
BEGIN
    CREATE TABLE [PasswordResetChallenges] (
        [Email] nvarchar(254) NOT NULL,
        [CodeHash] varchar(64) NULL,
        [CodeExpiresAt] datetimeoffset NOT NULL,
        [ResendAt] datetimeoffset NOT NULL,
        [AttemptsLeft] int NOT NULL,
        CONSTRAINT [PK_PasswordResetChallenges] PRIMARY KEY ([Email])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111120034_AddPasswordReset'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111120034_AddPasswordReset', N'9.0.21');
END;

COMMIT;
GO
