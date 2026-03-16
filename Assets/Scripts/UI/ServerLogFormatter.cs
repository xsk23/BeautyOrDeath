using UnityEngine;
using System;

/// <summary>
/// 自动为所有 Debug.Log 加上时间戳的处理器
/// </summary>
public class ServerLogFormatter : ILogHandler
{
    private readonly ILogHandler m_DefaultLogHandler = Debug.unityLogger.logHandler;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Install()
    {
        // 【新增】如果是服务器/无显卡模式，强制控制台使用 UTF-8
        if (Application.isBatchMode)
        {
            System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        // 替换默认的日志处理器
        Debug.unityLogger.logHandler = new ServerLogFormatter();
        Debug.Log($"<color=green>[System]</color> Log Formatter Installed. Timestamping enabled.");
    }

    public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
    {
        // 1. 获取当前时间戳 (包含毫秒，方便排查同步问题)
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        // 2. 格式化原始消息
        string originalMessage = string.Format(format, args);

        // 3. 拼接时间戳头
        // 格式示例: [2023-10-27 10:00:01.123] [Log] Your Message
        string decoratedMessage = $"[{timestamp}] [{logType}] {originalMessage}";

        // 4. 调用原生处理器输出（确保 context 依然有效，点击 log 依然能跳转到物体）
        m_DefaultLogHandler.LogFormat(logType, context, "{0}", decoratedMessage);
    }

    public void LogException(Exception exception, UnityEngine.Object context)
    {
        // 异常处理保持原样，但可以根据需要在这里也加上时间戳的打印
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        Debug.Log($"[{timestamp}] [Exception] Incoming Exception:");
        
        m_DefaultLogHandler.LogException(exception, context);
    }
}