#!/bin/bash

BINARY_NAME="build_linux.x86_64"
LINK_NAME="MyGameServer.exe"
LOG_FILE="server_log.txt"

echo "======= 猎人服务器启动中 ======="

# 1. 权限与软连接
chmod +x "$BINARY_NAME"
ln -sf "$BINARY_NAME" "$LINK_NAME"
# 递归确保 Data 文件夹和子文件有读取权限
chmod -R 755 .

# 2. 清理旧进程
pkill -9 -f "$BINARY_NAME"

# 3. 启动检查：确保 Data 文件夹存在
if [ ! -d "${BINARY_NAME%%.*}_Data" ]; then
    echo "错误：找不到资源文件夹 ${BINARY_NAME%%.*}_Data！请重新上传该文件夹。"
    exit 1
fi

# 4. 启动
echo "正在启动进程..."
nohup ./"$BINARY_NAME" -batchmode -nographics -logFile "$LOG_FILE" > /dev/null 2>&1 &

# 5. 动态观察启动结果
echo "等待初始化..."
sleep 3

if pgrep -f "$BINARY_NAME" > /dev/null
then
    echo "======= 成功：服务器已进入后台运行 ======="
    echo "最近的日志内容："
    tail -n 10 "$LOG_FILE"
else
    echo "======= 失败：进程未启动，请查看错误详情： ======="
    cat "$LOG_FILE"
fi