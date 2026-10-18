# Downloads the latest Android APK built by CI from develop (android-dev pre-release)
# next to the Windows app sources, so that local runs serve it to phones.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'  # the progress bar makes Invoke-WebRequest very slow
$target = Join-Path $PSScriptRoot '..\windows\src\MonAlbumPc.App\Android'
New-Item -ItemType Directory -Force $target | Out-Null
$base = 'https://github.com/lgm42/mon-album-pc/releases/download/android-dev'
foreach ($file in 'monalbumpc.apk', 'monalbumpc.apk.json') {
    Invoke-WebRequest "$base/$file" -OutFile (Join-Path $target $file)
}
Get-Content (Join-Path $target 'monalbumpc.apk.json')
