@echo off
chcp 65001 >nul
cd /d %~dp0

echo ============================================
echo  DIP 物料管理系统
echo  后端: http://localhost:8400
echo  Swagger: http://localhost:8400/swagger
echo  关闭此窗口即可停止服务
echo ============================================

cd backend\DIP.API
start http://localhost:8400
dotnet run --urls=http://0.0.0.0:8400
pause
