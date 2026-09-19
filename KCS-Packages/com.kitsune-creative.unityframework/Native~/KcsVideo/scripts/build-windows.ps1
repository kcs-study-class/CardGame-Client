# KcsVideo.dll (x64) をビルドして Runtime/Plugins/x86_64/ に配置する
#
# 前提: Visual Studio 2022/18 (C++ デスクトップ開発 + vcpkg コンポーネント)
# 使い方 (PowerShell):
#   .\scripts\build-windows.ps1                 # libvpx を vcpkg でビルド (初回のみ数分) → DLL ビルド → 配置
#   .\scripts\build-windows.ps1 -SkipVcpkg      # libvpx ビルド済みなら省略
#
# vcpkg のビルド成果物は $VcpkgCache (既定: ~\vcpkg-cache) に置く (リポジトリ外)。
# ※ VS 同梱の vcpkg はマニフェストモード専用。pkgconf の GitHub tarball が cmake -E tar で展開できない
#    環境があるため、vcpkg-overlays/pkgconf (配布元 tar.xz を使う) をオーバーレイしている。
param(
    [switch]$SkipVcpkg,
    [string]$VcpkgCache = (Join-Path $env:USERPROFILE "vcpkg-cache"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot          # Native~/KcsVideo
$pluginDir = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) "Runtime\Plugins\x86_64"

# ---- Visual Studio の場所 ----
$vsCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\18\Community",
    "C:\Program Files\Microsoft Visual Studio\18\Professional",
    "C:\Program Files\Microsoft Visual Studio\18\Enterprise",
    "C:\Program Files\Microsoft Visual Studio\2022\Community",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise"
)
$vs = $vsCandidates | Where-Object { Test-Path (Join-Path $_ "VC\vcpkg\vcpkg.exe") } | Select-Object -First 1
if (-not $vs) { throw "Visual Studio (vcpkg コンポーネント付き) が見つかりません" }
$vcpkg = Join-Path $vs "VC\vcpkg\vcpkg.exe"
$cmake = Join-Path $vs "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$env:VCPKG_ROOT = Join-Path $vs "VC\vcpkg"
Write-Host "VS     : $vs"
Write-Host "cache  : $VcpkgCache"

# ---- libvpx (vcpkg, x64 static) ----
$triplet = "x64-windows-static"
$installRoot = Join-Path $VcpkgCache "installed"
if (-not $SkipVcpkg) {
    New-Item -ItemType Directory -Force $VcpkgCache | Out-Null
    Push-Location $root
    try {
        & $vcpkg install --triplet $triplet `
            "--overlay-ports=$root\vcpkg-overlays" `
            "--x-buildtrees-root=$VcpkgCache\buildtrees" `
            "--downloads-root=$VcpkgCache\downloads" `
            "--x-packages-root=$VcpkgCache\packages" `
            "--x-install-root=$installRoot"
        if ($LASTEXITCODE -ne 0) { throw "vcpkg install failed ($LASTEXITCODE)" }
    } finally { Pop-Location }
}
$libvpxRoot = Join-Path $installRoot $triplet
if (-not (Test-Path (Join-Path $libvpxRoot "include\vpx\vpx_decoder.h"))) {
    throw "libvpx が見つかりません: $libvpxRoot (先に -SkipVcpkg 無しで実行してください)"
}

# ---- KcsVideo.dll ----
$buildDir = Join-Path $root "build\windows-x64"
& $cmake -S $root -B $buildDir -G "Visual Studio 17 2022" -A x64 "-DKCSV_LIBVPX_ROOT=$libvpxRoot" -DKCSV_SHARED=ON
if ($LASTEXITCODE -ne 0) {
    # VS 18 ではジェネレータ名が変わるためフォールバック
    & $cmake -S $root -B $buildDir -A x64 "-DKCSV_LIBVPX_ROOT=$libvpxRoot" -DKCSV_SHARED=ON
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }
}
& $cmake --build $buildDir --config $Configuration
if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }

$dll = Get-ChildItem -Path $buildDir -Recurse -Filter "KcsVideo.dll" | Where-Object { $_.FullName -like "*\$Configuration\*" } | Select-Object -First 1
if (-not $dll) { throw "KcsVideo.dll が見つかりません" }
New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item $dll.FullName (Join-Path $pluginDir "KcsVideo.dll") -Force
Write-Host "deployed: $(Join-Path $pluginDir 'KcsVideo.dll')"
