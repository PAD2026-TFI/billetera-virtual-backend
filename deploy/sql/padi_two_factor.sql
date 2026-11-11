BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111015222_AddTwoFactorAuthentication'
)
BEGIN
    ALTER TABLE [Users] ADD [TwoFactorEnabledAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111015222_AddTwoFactorAuthentication'
)
BEGIN
    ALTER TABLE [Users] ADD [TwoFactorLastUsedStep] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111015222_AddTwoFactorAuthentication'
)
BEGIN
    ALTER TABLE [Users] ADD [TwoFactorSecret] nvarchar(512) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111015222_AddTwoFactorAuthentication'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111015222_AddTwoFactorAuthentication', N'9.0.21');
END;

COMMIT;
GO

