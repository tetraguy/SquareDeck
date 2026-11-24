# Makes a code-signing certificate for the widget package (with the Publisher from Package.appxmanifest), trusts it on this PC, and saves
# SquareDeck.Widget.pfx (to sign with) and SquareDeck.Widget.cer (to trust it on another PC) next to this script.
# Run in PowerShell as administrator (trusting a certificate needs it). Don't commit the .pfx.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
[xml]$manifest = Get-Content (Join-Path $here 'SquareDeck.Widget\Package.appxmanifest')
$publisher = $manifest.Package.Identity.Publisher
$password = Read-Host -AsSecureString 'Password for the .pfx'
$cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName 'SquareDeck widget' `
    -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
Export-PfxCertificate -Cert $cert -FilePath (Join-Path $here 'SquareDeck.Widget.pfx') -Password $password | Out-Null
Export-Certificate -Cert $cert -FilePath (Join-Path $here 'SquareDeck.Widget.cer') | Out-Null
Import-Certificate -FilePath (Join-Path $here 'SquareDeck.Widget.cer') -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
Write-Host "Done: SquareDeck.Widget.pfx and SquareDeck.Widget.cer are in $here, and this PC trusts the certificate."
