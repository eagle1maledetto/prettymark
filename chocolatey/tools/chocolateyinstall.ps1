$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  fileType       = 'exe'
  url64bit       = 'https://gitlab.com/api/v4/projects/eagle1%2Fprettymark/packages/generic/prettymark/1.0.0/PrettyMark-Setup-1.0.0-win-x64.exe'
  checksum64     = '2932C1AF107F45DF3C02C745CD6B9598FAE2FFF7E9A208205E42B879A90F4499'
  checksumType64 = 'sha256'
  silentArgs     = '/S'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
