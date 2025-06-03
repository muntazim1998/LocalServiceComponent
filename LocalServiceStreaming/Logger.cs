using System;
using System.IO;

public static class Logger
{
    private static readonly string logDirectory;
    private static readonly object lockObj = new();

    static Logger()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        logDirectory = Path.Combine(localAppData, "logs");

        if (!Directory.Exists(logDirectory))
            Directory.CreateDirectory(logDirectory);
    }

    private static string GetLogFilePath()
    {
        if (!Directory.Exists(logDirectory))
            Directory.CreateDirectory(logDirectory);
        string fileName = $"{DateTime.Now:yyyy-MM-dd}.log";
        return Path.Combine(logDirectory, fileName);
    }

    public static void Info(string message)
    {
        WriteLog("INFO", message);
    }

    public static void Error(string message, Exception ex = null)
    {
        string fullMessage = message + (ex != null ? $"\nException: {ex}" : "");
        WriteLog("ERROR", fullMessage);
    }

    private static void WriteLog(string level, string message)
    {
        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {level} | {message}";
        string filePath = GetLogFilePath();

        try
        {
            lock (lockObj)
            {
                File.AppendAllText(filePath, logEntry + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}
