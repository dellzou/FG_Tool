# build_exe.ps1 —— 用系统自带的 C# 编译器打包单文件 exe，不需要 Visual Studio
#
# 用法：powershell -ExecutionPolicy Bypass -File .\build_exe.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "找不到 csc.exe，请确认已安装 .NET Framework 4.x"
}

Write-Host "编译器: $csc"

& $csc /nologo /target:winexe /platform:anycpu /optimize+ `
    /out:FG_Tool.exe `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    FgTool.cs

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码 $LASTEXITCODE"
}

$out = Join-Path $root "FG_Tool.exe"
$size = (Get-Item $out).Length
$hash = (Get-FileHash $out -Algorithm SHA256).Hash.ToLower()
Write-Host ""
Write-Host "编译完成: $out"
Write-Host "大小: $size 字节"
Write-Host "SHA256: $hash"
