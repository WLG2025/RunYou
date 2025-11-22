// using System;
// using System.Windows.Forms; // 隐式using后无需手动引入
using System.Diagnostics;  // 添加这一行来使用 ProcessStartInfo
using System.Text.Json;

namespace p2
{
    // 简单的日志记录类
    public class Logger
    {
        private readonly string _logFilePath;
        private readonly string _fileNameBase = string.Empty;
        private string _lastDate = string.Empty;
        private readonly object _lockObject = new();
        private readonly int _processId;
        public Logger(string fileName)
        {
            string logDirectory = Path.Combine(
                "./",
                "log"
            );
            Directory.CreateDirectory(logDirectory);

            _logFilePath = logDirectory;
            _fileNameBase = fileName.Split(".log")[0];
            _lastDate = DateTime.Now.ToString("yyyyMMdd");
            _processId = Environment.ProcessId;
        }

        public void Debug(string message)
        {
            WriteLog("DEBUG", message);
        }

        public void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public void Warn(string message)
        {
            WriteLog("WARN", message);
        }

        public void Error(string message)
        {
            WriteLog("ERROR", message);
        }

        private void WriteLog(string level, string message)
        {
            try
            {
                lock (_lockObject)
                {
                    string nowDate = DateTime.Now.ToString("yyyyMMdd");
                    if (nowDate != _lastDate)
                    {
                        _lastDate = nowDate;
                    }
                    string logFilePath = Path.Combine(_logFilePath, $"{_fileNameBase}_{_lastDate}.log");
                    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}|{_processId}|{level}|{message}{Environment.NewLine}";
                    File.AppendAllText(logFilePath, logEntry);
                }
            }
            catch
            {
                // 忽略日志记录错误
            }
        }
    }

    public class Helper
    {
        private static readonly Logger logger = Program.GetLogger();
        public static void KillProcessById(int processId)
        {
            if (processId <= 0)
            {
                return;
            }
            // 使用 taskkill 命令终止整个进程树
            ProcessStartInfo killInfo = new()
            {
                FileName = "taskkill",
                Arguments = $"/PID {processId} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using Process killProcess = Process.Start(killInfo)!; // !号告诉编译器这里不为null
            killProcess.WaitForExit(5000); // 等待最多5秒

            // 可选：读取输出和错误信息
            string output = killProcess.StandardOutput.ReadToEnd();
            string error = killProcess.StandardError.ReadToEnd();

            if (killProcess?.ExitCode == 0)
            {
                logger.Info($"成功终止进程树:\n{output}");
            }
            else
            {
                logger.Warn($"终止进程树时有警告: {error}");
            }
        }
    }

    public class TaskWrapper(string taskDesc, string fileName, string processParam, Button button)
    {
        private readonly string _taskDesc = taskDesc; // 任务描述
        private readonly string _processName = fileName; // 可执行程序
        private readonly string _processParam = processParam; // 程序参数
        private readonly Button _button = button; // 关联的按钮
        private Process? currentProcess = null;  // 记录当前进程对象
        private bool isTaskRunning = false;  // 添加成员变量标识任务状态
        private CancellationTokenSource? cancellationTokenSource = null;
        private static readonly Logger logger = Program.GetLogger();

        private void UpdateButtonText(string text)
        {
            _button?.Invoke((MethodInvoker)delegate
            {
                _button.Text = text;
            });
        }
        public bool CleanTask()
        {
            try
            {
                if (isTaskRunning && (currentProcess != null))
                {
                    UpdateButtonText("stopping..");
                    if (!currentProcess.HasExited)
                    {
                        logger.Info($"正在终止后台进程..{currentProcess?.Id}");
                        Helper.KillProcessById(currentProcess!.Id);
                        UpdateButtonText(_taskDesc);
                        logger.Info($"后台进程已终止:{currentProcess?.Id}");
                    }
                    // 取消任务
                    cancellationTokenSource?.Cancel();
                    return true;
                }
                else if (isTaskRunning)
                {
                    logger.Info($"任务已运行，请勿重复运行:{currentProcess?.Id}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"终止进程时出错: {ex.Message}|{currentProcess?.Id}");
                return true;
            }
            return false;
        }
        public void DoTask()
        {
            if (CleanTask())
            {
                return;
            }

            isTaskRunning = true;
            UpdateButtonText("停止" + _taskDesc);
            logger.Info("任务状态设置为运行中");

            cancellationTokenSource?.Dispose();
            // 创建取消令牌
            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;
            _ = Task.Run(() =>
            {
                try
                {
                    // 创建 ProcessStartInfo 对象
                    ProcessStartInfo startInfo = new()
                    {
                        // FileName = "python", // Python 解释器路径
                        // Arguments = "--version", // 要执行的 Python 命令参数
                        FileName = _processName,
                        Arguments = _processParam,

                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    // 额外保障：设置环境变量，确保print输出可以立即输出到日志
                    startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                    // 创建并启动进程
                    using Process process = Process.Start(startInfo)!;
                    currentProcess = process; // 记录进程对象
                    logger.Info($"子进程已运行:{process?.Id}");

                    // 读取输出
                    // string output = process.StandardOutput.ReadToEnd();
                    // string error = process.StandardError.ReadToEnd();
                    // // 显示结果
                    // if (process.ExitCode == 0)
                    // {
                    //     // MessageBox.Show($"Python 命令执行成功:\n{output}");
                    //     logger.Info($"Python 命令执行成功:\n{output}");
                    // }
                    // else
                    // {
                    //     // MessageBox.Show($"Python 命令执行失败:\n{error}");
                    //     logger.Error($"Python 命令执行失败:\n{error}");
                    // }
                    // 设置异步输出处理
                    process!.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            logger.Info($"[Child Stdout][{process?.Id}] {e.Data}");
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            logger.Error($"[Child Stderr][{process?.Id}] {e.Data}");
                        }
                    };

                    // 开始异步读取
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // 等待进程结束，同时检查取消请求
                    while (!process.HasExited)
                    {
                        // 等待100ms或直到进程退出
                        if (process.WaitForExit(100))
                        {
                            logger.Info($"检测到进程已退出:{process.Id}");
                            break; // 进程正常退出
                        }

                        // 检查取消请求
                        if (token.IsCancellationRequested)
                        {
                            logger.Info($"收到取消请求，正在终止进程:{process.Id}");
                            if (!process.HasExited)
                            {
                                logger.Info($"取消时终止进程..{process.Id}");
                                // process.Kill();
                                Helper.KillProcessById(process.Id);
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                    {
                        logger.Info("任务已被取消");
                    }
                    else
                    {
                        logger.Info($"执行出错: {ex.Message}");
                    }
                }
                finally
                {
                    // 重置任务运行状态
                    // ResetTaskStatus();
                    isTaskRunning = false;
                    currentProcess = null;
                    UpdateButtonText(_taskDesc);
                    logger.Info("任务状态已重置");
                    logger.Info("该任务结束");
                }
            }, token).ContinueWith(task =>
            {
                logger.Info("任务完成后回调");
            });
        }
    }

    class Program
    {
        private static readonly Logger logger = new("admin");
        private static TaskWrapper? taskWrapper = null;
        private static bool appExited = false;
        private static Point startPos = new(0, 0);

        public static Logger GetLogger()
        {
            return logger;
        }

        public static void LoadTask(string appDirectory, string[] taskInfo)
        {
            string taskFile = Path.Combine(appDirectory, "task.txt");
            if (!File.Exists(taskFile))
            {
                return;
            }
            try
            {
                string[] lines = File.ReadAllLines(taskFile);
                if (lines.Length > 0)
                {
                    string[] parts = lines[0].Split('|');
                    if (parts.Length >= 3)
                    {
                        taskInfo[0] = parts[0]; // 任务描述
                        taskInfo[1] = parts[1]; // 可执行程序
                        taskInfo[2] = parts[2]; // 程序参数
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"读取任务配置出错: {ex.Message}");
            }
        }

        public static void LoadConf(string appDirectory)
        {
            // 读取json配置文件
            string confFile = Path.Combine(appDirectory, "app.json");
            if (!File.Exists(confFile))
            {
                return;
            }

            try
            {
                string jsonString = File.ReadAllText(confFile);
                JsonDocument doc = JsonDocument.Parse(jsonString);
                
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("startPos", out JsonElement startPosElement))
                {
                    string value = startPosElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(value))
                    {
                        var info = value.Split(',');
                        if (info.Length >= 2)
                        {
                            if (int.TryParse(info[0].Trim(), out int x) && int.TryParse(info[1].Trim(), out int y))
                            {
                                startPos.X = x;
                                startPos.Y = y;
                                logger.Info($"启动位置: ({x},{y})");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"解析配置文件出错: {ex.Message}");
            }
        }

        public static Form InitApp()
        {
            // 获取当前程序所在目录
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            // 切换到程序所在目录
            Directory.SetCurrentDirectory(appDirectory);
            // 记录程序启动日志
            logger.Info($"应用程序启动|{appDirectory}");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ApplicationExit += (sender, e) =>
            {
                logger.Info($"应用程序退出事件触发: {e}|{sender}");
                logger.Info("资源清理完成2");
                appExited = true;
            };

            LoadConf(appDirectory);

            return InitUI(appDirectory);
        }
        public static Form InitUI(string appDirectory)
        {
            string[] taskInfo = [
                "Show Win Ver",
                "winver",
                "--admin",
            ];
            LoadTask(appDirectory, taskInfo);
            // 创建按钮
            Button button = new()
            {
                Text = taskInfo[0],
                Size = new Size(100, 60),
                Location = new Point(100, 70),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Standard,
            };
            taskWrapper = new TaskWrapper(
                taskInfo[0],
                taskInfo[1],
                taskInfo[2],
                button
            );

            // 创建主窗口
            Form form = new()
            {
                Text = "Admin Tool",
                Size = new Size(300, 200),
                StartPosition = FormStartPosition.CenterScreen
            };

            {
                button.Location = new Point(
                    (form.ClientSize.Width - button.Width) / 2,
                    (form.ClientSize.Height - button.Height) / 2
                );
                // 添加点击事件处理
                button.Click += (sender, e) =>
                {
                    logger.Info($"按钮被点击:{sender}|{e}");
                    taskWrapper.DoTask();
                    logger.Info($"按钮点击处理完成");
                };
                // 将按钮添加到窗体
                form.Controls.Add(button);
            }

            AddNormalMenu(form);

            {
                if (startPos.X != 0 && startPos.Y != 0)
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = startPos;
                }

                // 添加窗体关闭事件处理
                form.FormClosing += (sender, e) =>
                {
                    try
                    {
                        logger.Info($"窗口正在关闭，关闭原因: {e.CloseReason}|{sender}");
                        taskWrapper.CleanTask();
                        logger.Info("资源清理完成1");
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"清理资源时出错: {ex.Message}");
                    }
                };
            }

            return form;
        }
        public static void AddNormalMenu(Form form)
        {
            // 创建菜单项
            ToolStripMenuItem optionMenu = new("选项");
            {
                ToolStripMenuItem helloMenu = new("Hello");
                ToolStripMenuItem exitItem = new("退出");
                helloMenu.Click += (sender, e) =>
                {
                    MessageBox.Show("Hello World!");
                };
                exitItem.Click += (sender, e) => form.Close();

                // 将子项添加到文件菜单
                optionMenu.DropDownItems.Add(helloMenu);
                // 在退出菜单前增加分隔线
                optionMenu.DropDownItems.Add(new ToolStripSeparator());
                optionMenu.DropDownItems.Add(exitItem);
            }

            ToolStripMenuItem aboutMenu = new("关于");
            {
                ToolStripMenuItem verMenu = new("版本");
                verMenu.Click += (sender, e) =>
                {
                    MessageBox.Show("ver-20251122");
                };
                aboutMenu.DropDownItems.Add(verMenu);
            }

            // 创建菜单栏
            MenuStrip menuStrip = new();
            // 将菜单项添加到菜单栏
            menuStrip.Items.Add(optionMenu);
            menuStrip.Items.Add(aboutMenu);

            // 将菜单栏添加到窗体
            form.MainMenuStrip = menuStrip;
            form.Controls.Add(menuStrip);
        }
        public static void ExitApp()
        {
            // 等待应用程序退出事件处理完成
            while (!appExited)
            {
                Thread.Sleep(50);
            }
            // 记录程序关闭日志
            logger.Info("应用程序关闭");
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.Run(InitApp());
            ExitApp();
        }
    }
}
