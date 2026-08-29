$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\DLBeastSaveManager\DLBeastSaveManager.csproj'
$output = Join-Path $root 'publish'

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $output

if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $output 'DLBeastSaveManager.exe'
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Output ""
Write-Output "Published: $exe ($size MB)"
