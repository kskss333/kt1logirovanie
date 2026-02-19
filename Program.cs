using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TaskManager
{
    public class TaskItem
    {
        public string Title { get; set; }

        public TaskItem(string title)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
        }
    }

    public static class Logger
    {
        private static readonly TraceSwitch traceSwitch = new TraceSwitch("TaskManagerSwitch", "Control tracing")
        {
            Level = TraceLevel.Verbose
        };

        public static void LogInfo(string message)
        {
            WriteLine("[INFO]", message);
        }

        public static void LogWarning(string message)
        {
            WriteLine("[WARNING]", message);
        }

        public static void LogError(string message)
        {
            WriteLine("[ERROR]", message);
        }

        public static void LogTrace(string message)
        {
            if (traceSwitch.TraceVerbose)
            {
                WriteLine("[TRACE]", message);
            }
        }

        private static void WriteLine(string level, string message)
        {
            Console.WriteLine($"{level} {message}");
        }
    }

    class Program
    {
        static List<TaskItem> tasks = new List<TaskItem>();
        static bool isRunning = true;

        static void Main(string[] args)
        {
            Logger.LogInfo("Программа TaskManager запущена.");

            while (isRunning)
            {
                Console.Write("\nВведите команду (add, remove, list, exit): ");
                string? command = Console.ReadLine()?.Trim().ToLower();

                switch (command)
                {
                    case "add":
                        AddTask();
                        break;
                    case "remove":
                        RemoveTask();
                        break;
                    case "list":
                        ListTasks();
                        break;
                    case "exit":
                        ExitApp();
                        break;
                    default:
                        if (!string.IsNullOrEmpty(command))
                            Logger.LogWarning("Неизвестная команда.");
                        break;
                }
            }
        }

        static void AddTask()
        {
            Logger.LogTrace("Начало операции AddTask.");
            Console.Write("Введите название задачи: ");
            string? title = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(title))
            {
                Logger.LogError("Попытка добавления задачи с пустым названием.");
                Logger.LogTrace("Конец операции AddTask (неудачно).");
                return;
            }

            tasks.Add(new TaskItem(title));
            Logger.LogInfo($"Задача \"{title}\" успешно добавлена.");
            Logger.LogTrace("Конец операции AddTask.");
        }

        static void RemoveTask()
        {
            Logger.LogTrace("Начало операции RemoveTask.");
            Console.Write("Введите название задачи для удаления: ");
            string? title = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(title))
            {
                Logger.LogError("Попытка удаления задачи с пустым названием.");
                Logger.LogTrace("Конец операции RemoveTask (неудачно).");
                return;
            }

            var taskToRemove = tasks.FirstOrDefault(t => t.Title == title);

            if (taskToRemove != null)
            {
                tasks.Remove(taskToRemove);
                Logger.LogInfo($"Задача \"{title}\" успешно удалена.");
            }
            else
            {
                Logger.LogError($"Задача \"{title}\" не найдена.");
            }

            Logger.LogTrace("Конец операции RemoveTask.");
        }

        static void ListTasks()
        {
            Logger.LogTrace("Начало операции ListTasks.");

            if (tasks.Count == 0)
            {
                Logger.LogInfo("Список задач пуст.");
                Logger.LogTrace("Конец операции ListTasks.");
                return;
            }

            Logger.LogInfo($"Всего задач: {tasks.Count}");
            for (int i = 0; i < tasks.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {tasks[i].Title}");
            }

            Logger.LogTrace("Конец операции ListTasks.");
        }

        static void ExitApp()
        {
            Logger.LogInfo("Завершение работы программы.");
            isRunning = false;
        }
    }
}