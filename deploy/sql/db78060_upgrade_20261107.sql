-- TFI_ICS / Wallet: upgrade through PrepareLegacyDataForHostedUpgrade.
-- Import into the application database selected in the hosting panel (db78060).
-- Uses __EFMigrationsHistory to skip applied migrations. No credentials or USE statement.
-- Missing legacy CVU/alias values are generated without changing balances or IDs.
-- Empty legacy CUIT values remain unknown; real CUIT values remain unique.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO
IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 51000, 'Select the application database before importing this script.', 1;
GO

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
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019000100_InitialIdentity'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019000100_InitialIdentity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019000100_InitialIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261019000100_InitialIdentity', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025220743_CreateWallets'
)
BEGIN
    CREATE TABLE [Wallets] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Balance] decimal(18,2) NOT NULL,
        [Currency] char(3) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Wallets] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Wallets_Balance] CHECK ([Balance] >= 0),
        CONSTRAINT [CK_Wallets_Currency] CHECK ([Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'),
        CONSTRAINT [FK_Wallets_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025220743_CreateWallets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Wallets_UserId] ON [Wallets] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025220743_CreateWallets'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261025220743_CreateWallets', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026002300_DevelopmentFunding'
)
BEGIN
    CREATE TABLE [DevelopmentFundings] (
        [Id] uniqueidentifier NOT NULL,
        [WalletId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] char(3) NOT NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_DevelopmentFundings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DevelopmentFundings_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_DevelopmentFundings_Currency] CHECK ([Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'),
        CONSTRAINT [FK_DevelopmentFundings_Wallets_WalletId] FOREIGN KEY ([WalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026002300_DevelopmentFunding'
)
BEGIN
    CREATE TABLE [Movements] (
        [Id] uniqueidentifier NOT NULL,
        [WalletId] uniqueidentifier NOT NULL,
        [Type] nvarchar(16) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [BalanceAfter] decimal(18,2) NOT NULL,
        [Source] nvarchar(32) NOT NULL,
        [DevelopmentFundingId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Movements] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Movements_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_Movements_BalanceAfter] CHECK ([BalanceAfter] >= 0),
        CONSTRAINT [CK_Movements_Source] CHECK ([DevelopmentFundingId] IS NOT NULL AND [Source] = 'DevelopmentFunding' AND [Type] = 'Credit'),
        CONSTRAINT [FK_Movements_DevelopmentFundings_DevelopmentFundingId] FOREIGN KEY ([DevelopmentFundingId]) REFERENCES [DevelopmentFundings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Movements_Wallets_WalletId] FOREIGN KEY ([WalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026002300_DevelopmentFunding'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DevelopmentFundings_IdempotencyKey] ON [DevelopmentFundings] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026002300_DevelopmentFunding'
)
BEGIN
    CREATE INDEX [IX_DevelopmentFundings_WalletId] ON [DevelopmentFundings] ([WalletId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026002300_DevelopmentFunding'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Movements_DevelopmentFundingId] ON [Movements] ([DevelopmentFundingId]) WHERE [DevelopmentFundingId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026002300_DevelopmentFunding'
)
BEGIN
    CREATE INDEX [IX_Movements_WalletId_CreatedAt_Id] ON [Movements] ([WalletId], [CreatedAt], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026002300_DevelopmentFunding'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026002300_DevelopmentFunding', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026060400_Transfers'
)
BEGIN
    ALTER TABLE [Movements] DROP CONSTRAINT [CK_Movements_Source];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026060400_Transfers'
)
BEGIN
    ALTER TABLE [Movements] ADD [TransferId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026060400_Transfers'
)
BEGIN
    CREATE TABLE [Transfers] (
        [Id] uniqueidentifier NOT NULL,
        [SourceWalletId] uniqueidentifier NOT NULL,
        [DestinationWalletId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] char(3) NOT NULL,
        [Status] nvarchar(16) NOT NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Transfers] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Transfers_Amount] CHECK ([Amount] > 0 AND [Amount] <= 780400),
        CONSTRAINT [CK_Transfers_Currency] CHECK ([Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'),
        CONSTRAINT [CK_Transfers_Status] CHECK ([Status] = 'Completed'),
        CONSTRAINT [CK_Transfers_Wallets] CHECK ([SourceWalletId] <> [DestinationWalletId]),
        CONSTRAINT [FK_Transfers_Wallets_DestinationWalletId] FOREIGN KEY ([DestinationWalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Transfers_Wallets_SourceWalletId] FOREIGN KEY ([SourceWalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026060400_Transfers'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Movements_TransferId_WalletId_Type] ON [Movements] ([TransferId], [WalletId], [Type]) WHERE [TransferId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026060400_Transfers'
)
BEGIN
    EXEC(N'ALTER TABLE [Movements] ADD CONSTRAINT [CK_Movements_Source] CHECK (([DevelopmentFundingId] IS NOT NULL AND [TransferId] IS NULL AND [Source] = ''DevelopmentFunding'' AND [Type] = ''Credit'') OR ([DevelopmentFundingId] IS NULL AND [TransferId] IS NOT NULL AND [Source] = ''Transfer'' AND [Type] IN (''Debit'', ''Credit'')))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026060400_Transfers'
)
BEGIN
    CREATE INDEX [IX_Transfers_DestinationWalletId] ON [Transfers] ([DestinationWalletId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026060400_Transfers'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Transfers_SourceWalletId_IdempotencyKey] ON [Transfers] ([SourceWalletId], [IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026060400_Transfers'
)
BEGIN
    ALTER TABLE [Movements] ADD CONSTRAINT [FK_Movements_Transfers_TransferId] FOREIGN KEY ([TransferId]) REFERENCES [Transfers] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026060400_Transfers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026060400_Transfers', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    ALTER TABLE [Wallets] DROP CONSTRAINT [FK_Wallets_Users_UserId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    DROP INDEX [IX_Users_NormalizedEmail] ON [Users];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    EXEC sp_rename N'[Wallets].[UserId]', N'OwnerId', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    EXEC sp_rename N'[Wallets].[IX_Wallets_UserId]', N'IX_Wallets_OwnerId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Wallets]') AND [c].[name] = N'Currency');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Wallets] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [Wallets] ADD DEFAULT 'ARS' FOR [Currency];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Transfers]') AND [c].[name] = N'Currency');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Transfers] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [Transfers] ADD DEFAULT 'ARS' FOR [Currency];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DevelopmentFundings]') AND [c].[name] = N'Currency');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [DevelopmentFundings] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [DevelopmentFundings] ADD DEFAULT 'ARS' FOR [Currency];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    CREATE TABLE [Addresses] (
        [Id] uniqueidentifier NOT NULL,
        [Street] nvarchar(150) NOT NULL,
        [Number] nvarchar(20) NOT NULL,
        [City] nvarchar(100) NOT NULL,
        [State] nvarchar(100) NOT NULL,
        [Country] nvarchar(100) NOT NULL,
        [PostalCode] nvarchar(20) NOT NULL,
        [Latitude] decimal(18,6) NULL,
        [Longitude] decimal(18,6) NULL,
        [ValidationStatus] nvarchar(20) NOT NULL,
        [ExternalPlaceId] nvarchar(255) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ValidatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Addresses] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    CREATE TABLE [WalletOwners] (
        [Id] uniqueidentifier NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_WalletOwners] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    EXEC(N'INSERT INTO [WalletOwners] ([Id], [IsActive], [CreatedAt]) SELECT [Id], [IsActive], [CreatedAt] FROM [Users] WHERE [Id] NOT IN (SELECT [Id] FROM [WalletOwners]);');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'CreatedAt');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [Users] DROP COLUMN [CreatedAt];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'IsActive');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var4 + '];');
    ALTER TABLE [Users] DROP COLUMN [IsActive];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    DECLARE @var5 sysname;
    SELECT @var5 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'RowVersion');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var5 + '];');
    ALTER TABLE [Users] DROP COLUMN [RowVersion];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    CREATE TABLE [CardRequests] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [AddressId] uniqueidentifier NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [RequestedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_CardRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CardRequests_Addresses_AddressId] FOREIGN KEY ([AddressId]) REFERENCES [Addresses] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    CREATE TABLE [ExternalClients] (
        [Id] uniqueidentifier NOT NULL,
        [CompanyName] nvarchar(150) NOT NULL,
        [ContactEmail] nvarchar(254) NOT NULL,
        [ApiKeyHash] nchar(64) NOT NULL,
        [Description] nvarchar(500) NULL,
        [LastRotatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_ExternalClients] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExternalClients_WalletOwners_Id] FOREIGN KEY ([Id]) REFERENCES [WalletOwners] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]) WHERE [NormalizedEmail] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    CREATE INDEX [IX_CardRequests_AddressId] ON [CardRequests] ([AddressId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ExternalClients_ApiKeyHash] ON [ExternalClients] ([ApiKeyHash]) WHERE [ApiKeyHash] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    ALTER TABLE [Users] ADD CONSTRAINT [FK_Users_WalletOwners_Id] FOREIGN KEY ([Id]) REFERENCES [WalletOwners] ([Id]) ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    ALTER TABLE [Wallets] ADD CONSTRAINT [FK_Wallets_WalletOwners_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [WalletOwners] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031025350_PolymorphicWalletOwner'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031025350_PolymorphicWalletOwner', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031055630_AddPaymentLinks'
)
BEGIN
    CREATE TABLE [PaymentLinks] (
        [Id] uniqueidentifier NOT NULL,
        [DestinationWalletId] uniqueidentifier NOT NULL,
        [MerchantOwnerId] uniqueidentifier NULL,
        [Title] nvarchar(128) NOT NULL,
        [Description] nvarchar(512) NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] char(3) NOT NULL DEFAULT 'ARS',
        [Token] nvarchar(64) NOT NULL,
        [Status] nvarchar(16) NOT NULL,
        [Type] nvarchar(16) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NULL,
        CONSTRAINT [PK_PaymentLinks] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PaymentLinks_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_PaymentLinks_Currency] CHECK ([Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'),
        CONSTRAINT [FK_PaymentLinks_WalletOwners_MerchantOwnerId] FOREIGN KEY ([MerchantOwnerId]) REFERENCES [WalletOwners] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PaymentLinks_Wallets_DestinationWalletId] FOREIGN KEY ([DestinationWalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031055630_AddPaymentLinks'
)
BEGIN
    CREATE INDEX [IX_PaymentLinks_DestinationWalletId] ON [PaymentLinks] ([DestinationWalletId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031055630_AddPaymentLinks'
)
BEGIN
    CREATE INDEX [IX_PaymentLinks_MerchantOwnerId] ON [PaymentLinks] ([MerchantOwnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031055630_AddPaymentLinks'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentLinks_Token] ON [PaymentLinks] ([Token]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031055630_AddPaymentLinks'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031055630_AddPaymentLinks', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    ALTER TABLE [PaymentLinks] ADD [RowVersion] rowversion NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] uniqueidentifier NOT NULL,
        [PaymentLinkId] uniqueidentifier NOT NULL,
        [MerchantOwnerId] uniqueidentifier NOT NULL,
        [DestinationWalletId] uniqueidentifier NOT NULL,
        [PayerUserId] uniqueidentifier NULL,
        [PayerWalletId] uniqueidentifier NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] char(3) NOT NULL DEFAULT 'ARS',
        [Method] nvarchar(16) NOT NULL,
        [Status] nvarchar(16) NOT NULL,
        [IdempotencyKey] nvarchar(128) NULL,
        [RequestHash] nvarchar(128) NULL,
        [TransferId] uniqueidentifier NULL,
        [CardBrand] nvarchar(32) NULL,
        [CardLast4] char(4) NULL,
        [CardHolderName] nvarchar(128) NULL,
        [AuthorizationCode] nvarchar(64) NULL,
        [FailureReason] nvarchar(256) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CompletedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Payments_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_Payments_Currency] CHECK ([Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'),
        CONSTRAINT [FK_Payments_PaymentLinks_PaymentLinkId] FOREIGN KEY ([PaymentLinkId]) REFERENCES [PaymentLinks] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Payments_Transfers_TransferId] FOREIGN KEY ([TransferId]) REFERENCES [Transfers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Payments_Users_PayerUserId] FOREIGN KEY ([PayerUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Payments_WalletOwners_MerchantOwnerId] FOREIGN KEY ([MerchantOwnerId]) REFERENCES [WalletOwners] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Payments_Wallets_DestinationWalletId] FOREIGN KEY ([DestinationWalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Payments_Wallets_PayerWalletId] FOREIGN KEY ([PayerWalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    CREATE INDEX [IX_Payments_DestinationWalletId] ON [Payments] ([DestinationWalletId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    CREATE INDEX [IX_Payments_MerchantOwnerId_CreatedAt] ON [Payments] ([MerchantOwnerId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Payments_MerchantOwnerId_IdempotencyKey] ON [Payments] ([MerchantOwnerId], [IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    CREATE INDEX [IX_Payments_PayerUserId] ON [Payments] ([PayerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    CREATE INDEX [IX_Payments_PayerWalletId] ON [Payments] ([PayerWalletId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    CREATE INDEX [IX_Payments_PaymentLinkId] ON [Payments] ([PaymentLinkId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    CREATE INDEX [IX_Payments_TransferId] ON [Payments] ([TransferId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031083138_AddPaymentEntityAndB2BConsultation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031083138_AddPaymentEntityAndB2BConsultation', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107154835_AddCardsAndCardType'
)
BEGIN
    ALTER TABLE [CardRequests] ADD [Type] nvarchar(20) NOT NULL DEFAULT N'Debit';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107154835_AddCardsAndCardType'
)
BEGIN
    CREATE TABLE [Cards] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CardHolderName] nvarchar(150) NOT NULL,
        [Brand] nvarchar(50) NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [CardNumberLast4] nvarchar(4) NOT NULL,
        [ExpirationMonth] int NOT NULL,
        [ExpirationYear] int NOT NULL,
        [CardToken] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Cards] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Cards_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107154835_AddCardsAndCardType'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Cards_CardToken] ON [Cards] ([CardToken]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107154835_AddCardsAndCardType'
)
BEGIN
    CREATE INDEX [IX_Cards_UserId] ON [Cards] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107154835_AddCardsAndCardType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107154835_AddCardsAndCardType', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    ALTER TABLE [Wallets] ADD [Alias] nvarchar(40) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    ALTER TABLE [Wallets] ADD [Cvu] nvarchar(22) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    ALTER TABLE [Users] ADD [Dni] nvarchar(20) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    ALTER TABLE [Users] ADD [WalletId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    ALTER TABLE [ExternalClients] ADD [Cuit] nvarchar(11) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    ALTER TABLE [ExternalClients] ADD [WebhookSecret] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    ALTER TABLE [ExternalClients] ADD [WebhookUrl] nvarchar(2048) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    EXEC(N'DECLARE @lastCvuSuffix bigint = (
        SELECT ISNULL(MAX(TRY_CONVERT(bigint, RIGHT([Cvu], 14))), 0)
        FROM [Wallets] WHERE LEN([Cvu]) = 22 AND LEFT([Cvu], 8) = N''00001234'');
    DECLARE @missingCvus bigint = (SELECT COUNT_BIG(*) FROM [Wallets] WHERE NULLIF(LTRIM(RTRIM([Cvu])), N'''') IS NULL);
    IF @lastCvuSuffix > 99999999999999 - @missingCvus
        THROW 51000, ''No available CVU suffixes for the legacy wallet upgrade.'', 1;
    ;WITH [Missing] AS (
        SELECT [Id], ROW_NUMBER() OVER (ORDER BY [Id]) AS [Sequence]
        FROM [Wallets] WHERE NULLIF(LTRIM(RTRIM([Cvu])), N'''') IS NULL
    )
    UPDATE w SET [Cvu] = N''00001234'' + RIGHT(REPLICATE(N''0'', 14) + CONVERT(nvarchar(14), @lastCvuSuffix + m.[Sequence]), 14)
    FROM [Wallets] w INNER JOIN [Missing] m ON m.[Id] = w.[Id];
    UPDATE [Wallets] SET [Alias] = N''legacy.'' + REPLACE(CONVERT(nvarchar(36), [Id]), N''-'', N'''')
    WHERE NULLIF(LTRIM(RTRIM([Alias])), N'''') IS NULL;
    UPDATE u SET [WalletId] = w.[Id]
    FROM [Users] u INNER JOIN [Wallets] w ON w.[OwnerId] = u.[Id]
    WHERE u.[WalletId] IS NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Wallets_Alias] ON [Wallets] ([Alias]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Wallets_Cvu] ON [Wallets] ([Cvu]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ExternalClients_Cuit] ON [ExternalClients] ([Cuit]) WHERE [Cuit] <> ''''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107165204_UpdateDomainModelForOnboardingAndB2B'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107165204_UpdateDomainModelForOnboardingAndB2B', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202503_AssociateAddressesWithUsers'
)
BEGIN
    ALTER TABLE [Addresses] ADD [UserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202503_AssociateAddressesWithUsers'
)
BEGIN
    CREATE INDEX [IX_Addresses_UserId] ON [Addresses] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202503_AssociateAddressesWithUsers'
)
BEGIN
    ALTER TABLE [Addresses] ADD CONSTRAINT [FK_Addresses_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107202503_AssociateAddressesWithUsers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107202503_AssociateAddressesWithUsers', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107204322_SupportPendingCardDeliveryAddress'
)
BEGIN
    DECLARE @var6 sysname;
    SELECT @var6 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CardRequests]') AND [c].[name] = N'Status');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [CardRequests] DROP CONSTRAINT [' + @var6 + '];');
    ALTER TABLE [CardRequests] ALTER COLUMN [Status] nvarchar(32) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107204322_SupportPendingCardDeliveryAddress'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107204322_SupportPendingCardDeliveryAddress', N'9.0.21');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210656_PrepareLegacyDataForHostedUpgrade'
)
BEGIN
    EXEC(N'DECLARE @lastCvuSuffix bigint = (
        SELECT ISNULL(MAX(TRY_CONVERT(bigint, RIGHT([Cvu], 14))), 0)
        FROM [Wallets] WHERE LEN([Cvu]) = 22 AND LEFT([Cvu], 8) = N''00001234'');
    DECLARE @missingCvus bigint = (SELECT COUNT_BIG(*) FROM [Wallets] WHERE NULLIF(LTRIM(RTRIM([Cvu])), N'''') IS NULL);
    IF @lastCvuSuffix > 99999999999999 - @missingCvus
        THROW 51000, ''No available CVU suffixes for the legacy wallet upgrade.'', 1;
    ;WITH [Missing] AS (
        SELECT [Id], ROW_NUMBER() OVER (ORDER BY [Id]) AS [Sequence]
        FROM [Wallets] WHERE NULLIF(LTRIM(RTRIM([Cvu])), N'''') IS NULL
    )
    UPDATE w SET [Cvu] = N''00001234'' + RIGHT(REPLICATE(N''0'', 14) + CONVERT(nvarchar(14), @lastCvuSuffix + m.[Sequence]), 14)
    FROM [Wallets] w INNER JOIN [Missing] m ON m.[Id] = w.[Id];
    UPDATE [Wallets] SET [Alias] = N''legacy.'' + REPLACE(CONVERT(nvarchar(36), [Id]), N''-'', N'''')
    WHERE NULLIF(LTRIM(RTRIM([Alias])), N'''') IS NULL;
    UPDATE u SET [WalletId] = w.[Id]
    FROM [Users] u INNER JOIN [Wallets] w ON w.[OwnerId] = u.[Id]
    WHERE u.[WalletId] IS NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210656_PrepareLegacyDataForHostedUpgrade'
)
BEGIN
    EXEC(N'UPDATE [CardRequests] SET [Type] = N''Debit''
    WHERE NULLIF(LTRIM(RTRIM([Type])), N'''') IS NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210656_PrepareLegacyDataForHostedUpgrade'
)
BEGIN
    DROP INDEX [IX_ExternalClients_Cuit] ON [ExternalClients];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210656_PrepareLegacyDataForHostedUpgrade'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ExternalClients_Cuit] ON [ExternalClients] ([Cuit]) WHERE [Cuit] <> ''''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107210656_PrepareLegacyDataForHostedUpgrade'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261107210656_PrepareLegacyDataForHostedUpgrade', N'9.0.21');
END;

COMMIT;
GO
