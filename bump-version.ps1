# Bumps the version inside TunarrDummyStart.csproj

$csprojPath = Join-Path $PSScriptRoot "TunarrDummyStart/TunarrDummyStart.csproj"
if (-not (Test-Path $csprojPath)) {
    Write-Error "Could not find TunarrDummyStart.csproj at $csprojPath"
    exit 1
}

[xml]$xml = Get-Content $csprojPath

# Find the PropertyGroup that contains Version
$propertyGroup = $xml.Project.PropertyGroup | Where-Object { $_.Version -ne $null }

if ($null -eq $propertyGroup) {
    # Fallback to the first PropertyGroup if none contains Version
    $propertyGroup = $xml.Project.PropertyGroup
    if ($propertyGroup -is [array]) {
        $propertyGroup = $propertyGroup[0]
    }
}

if ($null -eq $propertyGroup.Version) {
    # Create Version if it does not exist
    $versionNode = $xml.CreateElement("Version")
    $versionNode.InnerText = "1.0.0"
    $propertyGroup.AppendChild($versionNode) | Out-Null
}

# Extract values as string
$versionStr = [string]$propertyGroup.Version
Write-Host "Current Version: $versionStr"

# Parse version (expecting major.minor.patch or major.minor)
$versionParts = $versionStr.Split('.')
$major = 1
$minor = 0
$patch = 0

if ($versionParts.Length -ge 1) { [int]::TryParse($versionParts[0], [ref]$major) | Out-Null }
if ($versionParts.Length -ge 2) { [int]::TryParse($versionParts[1], [ref]$minor) | Out-Null }
if ($versionParts.Length -ge 3) { 
    [int]::TryParse($versionParts[2], [ref]$patch) | Out-Null
    $patch++
} else {
    $patch = 1
}

$newVersion = "$major.$minor.$patch"
Write-Host "New Version: $newVersion"

# Update XML InnerText values
$propertyGroup.Version = $newVersion
if ($null -ne $propertyGroup.ProductVersion) { $propertyGroup.ProductVersion = $newVersion }
if ($null -ne $propertyGroup.FileVersion) { $propertyGroup.FileVersion = $newVersion }

# Save back
$xml.Save($csprojPath)

Write-Host "Updated Version in csproj successfully to $newVersion"
