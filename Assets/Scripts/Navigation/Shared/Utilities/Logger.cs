using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Navigation.Utilities
{
    public enum LogLevel
    {
        Debug, Info, Warning, Error, Fatal
    };

    public class Logger
    {
        /// <summary>
        /// Log等级，低于该等级的log信息不会被输出
        /// </summary>
        public LogLevel Level { get; set; }

        /// <summary>
        /// log文件名，不包含".log"后缀
        /// </summary>
        public string LogFileName => _logFileName;

        /// <summary>
        /// log文件存储文件夹，
        /// </summary>
        public string LogFileDirectory => _logFileDirectory;

        public bool ShowFileInfo { get; set; } = true;
        public bool ShowDateTime { get; set; } = true;
        public bool ShowLevel { get; set; } = true;
        public bool UseUnityDebug { get; set; } = false;

        private StreamWriter _stream;
        private string _logFileName;
        private string _logFileDirectory;
        private StringBuilder _sb;

        private void Log(LogLevel level, string content)
        {
            if (level < Level) return;
            if (_stream == null) return; // 未初始化

            // Format: DateTime [level] content => filePath: (lineNumber:columnNumber) 
            var currentTime = DateTime.Now.ToString("G"); // yyyy/MM/dd HH:mm:ss

            StackTrace st = new(true);
            StackFrame sf = st.GetFrame(2); // 0 -> Log(...), 1 -> Debug()/Info()/...  

            string filePath = "", lineNumber = "", columnNumber = "";
            if (sf != null)
            {
                filePath = sf.GetFileName();
                lineNumber = sf.GetFileLineNumber().ToString();
                columnNumber = sf.GetFileColumnNumber().ToString();
            }

            _sb.Clear();
            if (ShowDateTime) _sb.Append($"{currentTime} ");
            if (ShowLevel) _sb.Append($"[{level}] ");
            _sb.Append($"{content}");
            if (ShowFileInfo) _sb.Append($" => {filePath}: ({lineNumber}:{columnNumber})");

            _stream.WriteLine(_sb.ToString());
        }

        public Logger(string logFolderPath, string logName)
        {
            _logFileDirectory = logFolderPath;
            _logFileName = logName;

            if (!Directory.Exists(_logFileDirectory))
                Directory.CreateDirectory(_logFileDirectory);

            _sb = new StringBuilder();
            OpenStream();
        }

        private void OpenStream()
        {
            if (_stream != null)
                _stream.Close();

            _stream = new StreamWriter($"{_logFileDirectory}/{_logFileName}.log");
            _stream.AutoFlush = true;
        }

        public void Close() => _stream?.Close();

        #region log方法
        public void Debug(string content)
        {
            Log(LogLevel.Debug, content);
            if (UseUnityDebug)
                UnityEngine.Debug.Log(content);
        }

        public void Info(string content)
        {
            Log(LogLevel.Info, content);
            if (UseUnityDebug)
                UnityEngine.Debug.Log(content);
        }

        public void Warning(string content)
        {
            Log(LogLevel.Warning, content);
            if (UseUnityDebug)
                UnityEngine.Debug.LogWarning(content);
        }
        public void Error(string content)
        {
            Log(LogLevel.Error, content);
            if (UseUnityDebug)
                UnityEngine.Debug.LogError(content);
        }
        public void Fatal(string content)
        {
            Log(LogLevel.Fatal, content);
            if (UseUnityDebug)
                UnityEngine.Debug.LogError(content);
        }
        #endregion
    }
}