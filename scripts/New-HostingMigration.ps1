param(
    [string]$Output = "deploy/sql/db78060_upgrade_20261107.sql"
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $outputPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $Output))
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $outputPath))
    & dotnet ef migrations script 0 20261107210656_PrepareLegacyDataForHostedUpgrade --idempotent --project Wallet.Infrastructure --startup-project Wallet.Api --configuration Audit --output $outputPath
    if ($LASTEXITCODE -ne 0) { throw 'SQL migration generation failed.' }
    $header = @(
        '-- TFI_ICS / Wallet: upgrade through PrepareLegacyDataForHostedUpgrade.'
        '-- Import into the application database selected in the hosting panel (db78060).'
        '-- Uses __EFMigrationsHistory to skip applied migrations. No credentials or USE statement.'
        '-- Missing legacy CVU/alias values are generated without changing balances or IDs.'
        '-- Empty legacy CUIT values remain unknown; real CUIT values remain unique.'
        'SET QUOTED_IDENTIFIER ON;'
        'SET ANSI_NULLS ON;'
        'SET ANSI_PADDING ON;'
        'SET ANSI_WARNINGS ON;'
        'SET CONCAT_NULL_YIELDS_NULL ON;'
        'SET ARITHABORT ON;'
        'SET NUMERIC_ROUNDABORT OFF;'
        'SET XACT_ABORT ON;'
        'GO'
        "IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')"
        "    THROW 51000, 'Select the application database before importing this script.', 1;"
        'GO'
        ''
        ''
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText($outputPath, $header + [IO.File]::ReadAllText($outputPath).TrimEnd() + [Environment]::NewLine)
    Write-Output "Generated: $outputPath"
} finally { Pop-Location }
