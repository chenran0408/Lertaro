param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [string]$PublicKeyPath = (Join-Path $PSScriptRoot '..\release-signing-public-key.pem')
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:SIGNING_KEY)) {
    throw 'SIGNING_KEY is required. Refusing to publish unsigned update packages.'
}
$signer = [Security.Cryptography.ECDsa]::Create()
$verifier = [Security.Cryptography.ECDsa]::Create()
try {
    $signer.ImportFromPem($env:SIGNING_KEY)
    $verifier.ImportFromPem([IO.File]::ReadAllText($PublicKeyPath))
    if ([Convert]::ToBase64String($signer.ExportSubjectPublicKeyInfo()) -ne
        [Convert]::ToBase64String($verifier.ExportSubjectPublicKeyInfo())) {
        throw 'The signing key does not match the application verification key.'
    }
    foreach ($name in @('Lertaro-Portable.zip', 'Lertaro-Portable-arm64.zip')) {
        $path = Join-Path $PackageDirectory $name
        $bytes = [IO.File]::ReadAllBytes($path)
        $signature = $signer.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
        [IO.File]::WriteAllBytes($path + '.sig', $signature)
        $written = [IO.File]::ReadAllBytes($path + '.sig')
        if (!$verifier.VerifyData($bytes, $written, [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) {
            throw "Written signature verification failed: $name"
        }
        Write-Output "Signed and verified: $name"
    }
}
finally { $signer.Dispose(); $verifier.Dispose(); $env:SIGNING_KEY = $null }
