namespace Wallet.Infrastructure.Migrations;

// Keep these statements stable: historical and forward migrations both use this upgrade.
internal static class HostedUpgradeDataSql
{
    internal const string WalletIdentifiers = """
        DECLARE @lastCvuSuffix bigint = (
            SELECT ISNULL(MAX(TRY_CONVERT(bigint, RIGHT([Cvu], 14))), 0)
            FROM [Wallets] WHERE LEN([Cvu]) = 22 AND LEFT([Cvu], 8) = N'00001234');
        DECLARE @missingCvus bigint = (SELECT COUNT_BIG(*) FROM [Wallets] WHERE NULLIF(LTRIM(RTRIM([Cvu])), N'') IS NULL);
        IF @lastCvuSuffix > 99999999999999 - @missingCvus
            THROW 51000, 'No available CVU suffixes for the legacy wallet upgrade.', 1;
        ;WITH [Missing] AS (
            SELECT [Id], ROW_NUMBER() OVER (ORDER BY [Id]) AS [Sequence]
            FROM [Wallets] WHERE NULLIF(LTRIM(RTRIM([Cvu])), N'') IS NULL
        )
        UPDATE w SET [Cvu] = N'00001234' + RIGHT(REPLICATE(N'0', 14) + CONVERT(nvarchar(14), @lastCvuSuffix + m.[Sequence]), 14)
        FROM [Wallets] w INNER JOIN [Missing] m ON m.[Id] = w.[Id];
        UPDATE [Wallets] SET [Alias] = N'legacy.' + REPLACE(CONVERT(nvarchar(36), [Id]), N'-', N'')
        WHERE NULLIF(LTRIM(RTRIM([Alias])), N'') IS NULL;
        UPDATE u SET [WalletId] = w.[Id]
        FROM [Users] u INNER JOIN [Wallets] w ON w.[OwnerId] = u.[Id]
        WHERE u.[WalletId] IS NULL;
        """;

    internal const string CardRequestTypes = """
        UPDATE [CardRequests] SET [Type] = N'Debit'
        WHERE NULLIF(LTRIM(RTRIM([Type])), N'') IS NULL;
        """;
}
