param([switch]$Login)
$ErrorActionPreference = 'Stop'
# Credentials exist only in this process while obtaining a token. No config file is written.
if ($Login) {
    $username = Read-Host 'SmartLEAD ID'
    $secureValue = Read-Host 'SmartLEAD password (hidden)' -AsSecureString
} else {
    Write-Host 'Enter a SmartLEAD web-service token. For ID/password sign-in, run with -Login.'
    $secureValue = Read-Host 'SmartLEAD token (hidden)' -AsSecureString
}
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
try {
    $secret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    if ($Login) {
        try {
            $result = Invoke-RestMethod -Uri 'https://smartlead.hallym.ac.kr/login/token.php' -Method Post -MaximumRedirection 0 -TimeoutSec 15 -Body @{
                username = $username; password = $secret; service = 'moodle_mobile_app'
            }
        } catch { throw 'SmartLEAD sign-in request failed. Check network or use an administrator-issued token.' }
        if (-not $result.token) {
            if ($result.errorcode -eq 'invalidlogin') { throw 'SmartLEAD rejected the ID/password. Check your credentials; SSO-only accounts require an administrator-issued token.' }
            throw 'SmartLEAD did not issue a token. SSO/2FA-only accounts or disabled mobile services require an administrator-issued web-service token.'
        }
        $env:SMARTLEAD_TOKEN = $result.token
    } else { $env:SMARTLEAD_TOKEN = $secret }
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    $secret = $null; $secureValue = $null; $result = $null
}
$previousMode = $env:NOTIFICATION_MODE
try {
    $env:NOTIFICATION_MODE = 'smartlead'
    Write-Host 'Starting local SmartLEAD notification server. Keep this window open. Ctrl+C stops it.'
    & node (Join-Path $PSScriptRoot 'server.mjs')
} finally {
    $env:SMARTLEAD_TOKEN = $null
    $env:NOTIFICATION_MODE = $previousMode
}
