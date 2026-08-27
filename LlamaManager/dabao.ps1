# ============================================================
# LlamaManager 便携版打包脚本
# 用法：右键"使用 PowerShell 运行"，或在终端执行 .\publish.ps1
# 产物：项目根目录下的单个 LlamaManager.exe（自包含，免装运行时）
# ============================================================

# 注意：打包前先关闭正在运行的 LlamaManager（含托盘），否则文件被占用会失败

# 清理旧的发布目录
Write-Host "清理旧的发布目录..." -ForegroundColor Yellow
$publishDir = Join-Path $PSScriptRoot "bin\Release\net10.0-windows\win-x64\publish"
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
}

# 执行打包命令
Write-Host "开始打包..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n❌ 打包失败，请检查上方错误信息" -ForegroundColor Red
    pause
    exit 1
}

# 获取新生成的发布目录
$publishDir = Join-Path $PSScriptRoot "bin\Release\net10.0-windows\win-x64\publish"

# 调试符号不影响使用，删掉保持目录干净（想留着排查崩溃就注释掉这行）
Write-Host "清理调试文件..." -ForegroundColor Yellow
Remove-Item "$publishDir\*.pdb" -ErrorAction SilentlyContinue

# 验证生成的exe文件
$exe = Get-Item "$publishDir\LlamaManager.exe"
if ($null -eq $exe) {
    Write-Host "`n❌ 未找到生成的可执行文件" -ForegroundColor Red
    pause
    exit 1
}

# 复制到根目录
Write-Host "复制到项目根目录..." -ForegroundColor Yellow
$targetPath = Join-Path $PSScriptRoot "LlamaManager.exe"
Copy-Item "$publishDir\LlamaManager.exe" $targetPath -Force

# 删除临时发布目录
Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue

Write-Host "`n✅ 打包完成：$targetPath" -ForegroundColor Green
Write-Host "   大小：$([math]::Round((Get-Item $targetPath).Length/1MB,1)) MB"
Write-Host "   用法：双击运行即可，无需安装任何依赖"
pause
