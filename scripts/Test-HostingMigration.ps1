param([string]$SqlFile = 'deploy/sql/db78060_upgrade_20261107.sql')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
$server = '(localdb)\MSSQLLocalDB'
$testPrefix = 'WalletHostingUpgradeTest_'
$databases = @()
function Invoke-TestSql([string]$database, [string]$statement) {
    $connection = New-Object System.Data.SqlClient.SqlConnection "Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True"
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 60
        $command.CommandText = $statement
        return $command.ExecuteScalar()
    } finally { $connection.Dispose() }
}
function Invoke-TestFile([string]$database, [string]$file) {
    & sqlcmd -I -S $server -E -b -d $database -i $file
    if ($LASTEXITCODE -ne 0) { throw "SQL import failed for $database" }
}
try {
    $sqlPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $SqlFile))
    [void][IO.Directory]::CreateDirectory((Join-Path $projectRoot 'artifacts/hosting-upgrade'))
    foreach ($baseline in @('0', '20261026060400_Transfers', '20261031083138_AddPaymentEntityAndB2BConsultation', '20261107204322_SupportPendingCardDeliveryAddress')) {
        $dbName = $testPrefix + [Guid]::NewGuid().ToString('N')
        if ($dbName -notmatch '^WalletHostingUpgradeTest_[a-f0-9]{32}$') { throw 'Unexpected temporary database name' }
        [void](Invoke-TestSql 'master' "CREATE DATABASE [$dbName]")
        $databases += $dbName
        if ($baseline -ne '0') {
            $baselineFile = Join-Path $projectRoot "artifacts/hosting-upgrade/$baseline.sql"
            & dotnet ef migrations script 0 $baseline --idempotent --project Wallet.Infrastructure --startup-project Wallet.Api --configuration Audit --no-build --output $baselineFile
            if ($LASTEXITCODE -ne 0) { throw 'Baseline SQL generation failed' }
            Invoke-TestFile $dbName $baselineFile
            if ($baseline -eq '20261026060400_Transfers') {
                $seed = "INSERT INTO [Users] ([Id],[FirstName],[LastName],[Email],[NormalizedEmail],[PasswordHash],[Role],[IsActive],[SessionVersion],[CreatedAt]) VALUES ('11111111-1111-1111-1111-111111111111','Old','Disabled','old1@example.test','OLD1@EXAMPLE.TEST','hash','Client',0,7,'2020-01-02T00:00:00+00:00'),('22222222-2222-2222-2222-222222222222','Old','Active','old2@example.test','OLD2@EXAMPLE.TEST','hash','Client',1,2,'2021-03-04T00:00:00+00:00'); INSERT INTO [Wallets] ([Id],[UserId],[Balance],[Currency],[CreatedAt]) VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','11111111-1111-1111-1111-111111111111',123.45,'ARS',SYSUTCDATETIME()),('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','22222222-2222-2222-2222-222222222222',678.90,'ARS',SYSUTCDATETIME());"
                [void](Invoke-TestSql $dbName $seed)
            } else {
                $seed = "INSERT INTO [WalletOwners] ([Id],[IsActive],[CreatedAt]) VALUES ('11111111-1111-1111-1111-111111111111',0,'2020-01-02T00:00:00+00:00'),('22222222-2222-2222-2222-222222222222',1,'2021-03-04T00:00:00+00:00'),('33333333-3333-3333-3333-333333333333',1,SYSUTCDATETIME()),('44444444-4444-4444-4444-444444444444',1,SYSUTCDATETIME()); INSERT INTO [Users] ([Id],[FirstName],[LastName],[Email],[NormalizedEmail],[PasswordHash],[Role],[SessionVersion]) VALUES ('11111111-1111-1111-1111-111111111111','Old','Disabled','old1@example.test','OLD1@EXAMPLE.TEST','hash','Client',7),('22222222-2222-2222-2222-222222222222','Old','Active','old2@example.test','OLD2@EXAMPLE.TEST','hash','Client',2); INSERT INTO [Wallets] ([Id],[OwnerId],[Balance],[Currency],[CreatedAt]) VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','11111111-1111-1111-1111-111111111111',123.45,'ARS',SYSUTCDATETIME()),('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','22222222-2222-2222-2222-222222222222',678.90,'ARS',SYSUTCDATETIME()); INSERT INTO [ExternalClients] ([Id],[CompanyName],[ContactEmail],[ApiKeyHash]) VALUES ('33333333-3333-3333-3333-333333333333','Legacy 1','merchant1@example.test',REPLICATE('A',64)),('44444444-4444-4444-4444-444444444444','Legacy 2','merchant2@example.test',REPLICATE('B',64)); INSERT INTO [Addresses] ([Id],[Street],[Number],[City],[State],[Country],[PostalCode],[ValidationStatus],[CreatedAt]) VALUES ('55555555-5555-5555-5555-555555555555','Calle','1','Ciudad','Provincia','Argentina','1000','Pending',SYSUTCDATETIME()); INSERT INTO [CardRequests] ([Id],[UserId],[AddressId],[Status],[RequestedAt],[UpdatedAt]) VALUES ('66666666-6666-6666-6666-666666666666','11111111-1111-1111-1111-111111111111','55555555-5555-5555-5555-555555555555','Pending',SYSUTCDATETIME(),SYSUTCDATETIME());"
                if ($baseline -eq '20261107204322_SupportPendingCardDeliveryAddress') {
                    [void](Invoke-TestSql $dbName "DROP INDEX [IX_ExternalClients_Cuit] ON [ExternalClients]; CREATE UNIQUE INDEX [IX_ExternalClients_Cuit] ON [ExternalClients]([Cuit]) WHERE [Cuit] IS NOT NULL;")
                    $seed = $seed.Replace('([Id],[FirstName],[LastName],[Email],[NormalizedEmail],[PasswordHash],[Role],[SessionVersion])','([Id],[FirstName],[LastName],[Email],[NormalizedEmail],[PasswordHash],[Role],[SessionVersion],[Dni])').Replace("'Client',7)","'Client',7,'')").Replace("'Client',2)","'Client',2,'')")
                    $seed = $seed.Replace('([Id],[OwnerId],[Balance],[Currency],[CreatedAt])','([Id],[OwnerId],[Balance],[Currency],[CreatedAt],[Cvu],[Alias])').Replace("123.45,'ARS',SYSUTCDATETIME())","123.45,'ARS',SYSUTCDATETIME(),'','')").Replace("678.90,'ARS',SYSUTCDATETIME())","678.90,'ARS',SYSUTCDATETIME(),'0000123400000000000042','existing.keep')")
                    $seed = $seed.Replace('([Id],[CompanyName],[ContactEmail],[ApiKeyHash])','([Id],[CompanyName],[ContactEmail],[ApiKeyHash],[Cuit])').Replace("REPLICATE('A',64))","REPLICATE('A',64),'')").Replace("REPLICATE('B',64))","REPLICATE('B',64),'30712345678')")
                    $seed = $seed.Replace('[AddressId],[Status],[RequestedAt]','[AddressId],[Type],[Status],[RequestedAt]').Replace("'55555555-5555-5555-5555-555555555555','Pending'","'55555555-5555-5555-5555-555555555555','','Pending'")
                }
                [void](Invoke-TestSql $dbName $seed)
            }
        }
        Invoke-TestFile $dbName $sqlPath
        $verify = "IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId]='20261107210656_PrepareLegacyDataForHostedUpgrade') THROW 51000,'Missing target migration',1; IF COL_LENGTH('CardRequests','Status')<>64 THROW 51000,'Incorrect card order status length',1; IF COL_LENGTH('Addresses','UserId') IS NULL THROW 51000,'Missing address ownership column',1; IF EXISTS (SELECT 1 FROM [Wallets] WHERE LEN([Cvu])<>22 OR LEFT([Cvu],8)<>'00001234' OR LEN([Alias])<6) THROW 51000,'Invalid wallet identifiers',1;"
        if ($baseline -ne '0') {
            $verify += " IF (SELECT [Balance] FROM [Wallets] WHERE [Id]='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')<>123.45 OR (SELECT [Balance] FROM [Wallets] WHERE [Id]='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb')<>678.90 THROW 51000,'Balances changed',1; IF NOT EXISTS (SELECT 1 FROM [WalletOwners] WHERE [Id]='11111111-1111-1111-1111-111111111111' AND [IsActive]=0 AND [CreatedAt]='2020-01-02T00:00:00+00:00') THROW 51000,'Disabled user metadata changed',1; IF (SELECT [SessionVersion] FROM [Users] WHERE [Id]='11111111-1111-1111-1111-111111111111')<>7 THROW 51000,'Session version changed',1; IF EXISTS (SELECT 1 FROM [Users] u INNER JOIN [Wallets] w ON w.[OwnerId]=u.[Id] WHERE u.[WalletId] IS NULL OR u.[WalletId]<>w.[Id]) THROW 51000,'User wallet link missing',1;"
        }
        if ($baseline -eq '20261031083138_AddPaymentEntityAndB2BConsultation') {
            $verify += " IF (SELECT COUNT(*) FROM [ExternalClients] WHERE [Cuit]='')<>2 THROW 51000,'Legacy CUIT was fabricated',1; IF (SELECT [Type] FROM [CardRequests] WHERE [Id]='66666666-6666-6666-6666-666666666666')<>'Debit' THROW 51000,'Legacy card request type missing',1; IF EXISTS (SELECT 1 FROM [Addresses] WHERE [UserId] IS NOT NULL) THROW 51000,'Legacy address owner was inferred',1;"
        }
        if ($baseline -eq '20261107204322_SupportPendingCardDeliveryAddress') {
            $verify += " IF NOT EXISTS (SELECT 1 FROM [Wallets] WHERE [Id]='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' AND [Cvu]='0000123400000000000042' AND [Alias]='existing.keep') THROW 51000,'Existing CVU or alias changed',1; IF (SELECT COUNT(*) FROM [ExternalClients] WHERE [Cuit]='')<>1 OR NOT EXISTS (SELECT 1 FROM [ExternalClients] WHERE [Cuit]='30712345678') THROW 51000,'Existing CUIT changed',1; IF (SELECT [Type] FROM [CardRequests] WHERE [Id]='66666666-6666-6666-6666-666666666666')<>'Debit' THROW 51000,'Legacy request type was not repaired',1;"
        }
        [void](Invoke-TestSql $dbName $verify)
        $before = Invoke-TestSql $dbName 'SELECT [Id],[OwnerId],[Balance],[Cvu],[Alias] FROM [Wallets] ORDER BY [Id] FOR JSON PATH'
        Invoke-TestFile $dbName $sqlPath
        $after = Invoke-TestSql $dbName 'SELECT [Id],[OwnerId],[Balance],[Cvu],[Alias] FROM [Wallets] ORDER BY [Id] FOR JSON PATH'
        if ($before -ne $after) { throw 'Second import modified existing wallets' }
        [void](Invoke-TestSql $dbName $verify)
        Write-Output "PASS: baseline $baseline; existing data preserved; second import idempotent."
    }
} finally {
    [System.Data.SqlClient.SqlConnection]::ClearAllPools()
    foreach ($dbName in $databases) {
        if ($dbName -notmatch '^WalletHostingUpgradeTest_[a-f0-9]{32}$') { throw 'Unsafe cleanup target' }
        [void](Invoke-TestSql 'master' "DROP DATABASE [$dbName]")
    }
    Pop-Location
}
