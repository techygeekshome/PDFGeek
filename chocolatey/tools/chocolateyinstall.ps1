$ErrorActionPreference = 'Stop'

# PDFGeek ships an Inno Setup installer. The package downloads it from the GitHub release for the
# matching tag and verifies it against a SHA-256 checksum rather than embedding the binary. Because
# nothing is embedded, this package must NOT contain a tools\VERIFICATION.txt - that file is only
# for packages that ship a binary inside the nupkg, and including one is what the USP 8.0.0
# submission was rejected for.
$packageArgs = @{
  packageName    = 'pdfgeek'
  fileType       = 'exe'
  url            = 'https://github.com/techygeekshome/PDFGeek/releases/download/v1.1.1/PDFGeekSetup.exe'
  checksum       = '97d137d53ef00625e2135db09909686863aa1306a1078e83fb6c54a97229a521'
  checksumType   = 'sha256'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
