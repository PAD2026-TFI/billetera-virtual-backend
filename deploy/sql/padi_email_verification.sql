BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110152252_AddRegistrationEmailVerification'
)
BEGIN
    CREATE TABLE [EmailVerificationChallenges] (
        [Email] nvarchar(254) NOT NULL,
        [CodeHash] varchar(64) NULL,
        [CodeExpiresAt] datetimeoffset NOT NULL,
        [ResendAt] datetimeoffset NOT NULL,
        [AttemptsLeft] int NOT NULL,
        [ProofHash] varchar(64) NULL,
        [ProofExpiresAt] datetimeoffset NULL,
        CONSTRAINT [PK_EmailVerificationChallenges] PRIMARY KEY ([Email])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110152252_AddRegistrationEmailVerification'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110152252_AddRegistrationEmailVerification', N'9.0.21');
END;

COMMIT;
GO
