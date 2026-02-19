using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace TaskManager
{
    public class TaskItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public PriorityLevel Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TaskStatus Status { get; set; }

        public TaskItem(string title, string description = "", PriorityLevel priority = PriorityLevel.Medium)
        {
            Id = Guid.NewGuid();
            Title = title;
            Description = description;
            Priority = priority;
            CreatedAt = DateTime.Now;
            Status = TaskStatus.Pending;
        }
    }

    public enum PriorityLevel
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public enum TaskStatus
    {
        Pending,
        InProgress,
        Completed,
        Cancelled
    }

    public static class OperationTracer
    {
        private static readonly Dictionary<string, (Stopwatch Watch, DateTime Start)> _operations
            = new Dictionary<string, (Stopwatch, DateTime)>();

        public static void Start(string operationName, string context = "")
        {
            var stopwatch = Stopwatch.StartNew();
            _operations[operationName] = (stopwatch, DateTime.Now);

            Log.Debug("[TRACE] >>> Начало операции: {Operation} | Контекст: {Context} | Время: {StartTime}",
                operationName, string.IsNullOrEmpty(context) ? "-" : context, DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        public static void Stop(string operationName, string result, string details = "")
        {
            if (_operations.TryGetValue(operationName, out var data))
            {
                var (stopwatch, startTime) = data;
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds;

                var logEvent = result.ToLower() switch
                {
                    "success" => LogEventLevel.Information,
                    "warning" => LogEventLevel.Warning,
                    "error" => LogEventLevel.Error,
                    "fatal" => LogEventLevel.Fatal,
                    _ => LogEventLevel.Information
                };

                Log.Write(logEvent,
                    "[TRACE] <<< Завершение операции: {Operation} | Результат: {Result} | Время выполнения: {ElapsedMs} мс | Детали: {Details}",
                    operationName, result, elapsedMs, details);

                _operations.Remove(operationName);
            }
        }

        public static void StopWithError(string operationName, Exception ex, string context)
        {
            if (_operations.TryGetValue(operationName, out var data))
            {
                var (stopwatch, _) = data;
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds;

                Log.Error(ex,
                    "[TRACE] <<< ОШИБКА в операции: {Operation} | Контекст: {Context} | Время: {ElapsedMs} мс | Тип: {ExceptionType} | Сообщение: {Message}",
                    operationName, context, elapsedMs, ex.GetType().Name, ex.Message);

                _operations.Remove(operationName);
            }
            else
            {
                Log.Error(ex,
                    "[TRACE] ОШИБКА (без трассировки): {Operation} | Контекст: {Context}",
                    operationName, context);
            }
        }
    }

    public static class GlobalExceptionHandler
    {
        public static void Subscribe()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Log.Fatal(ex, "[FATAL] Необработанное исключение AppDomain. IsTerminating: {IsTerminating}",
                    args.IsTerminating);
                Console.WriteLine("\n!!! КРИТИЧЕСКАЯ ОШИБКА ПРИЛОЖЕНИЯ !!!");
                Console.WriteLine("Приложение будет завершено. Обратитесь к администратору.");
            };

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Log.Error(args.Exception, "[FATAL] Необработанное исключение в Task");
                args.SetObserved();
            };
        }
    }

    public static class TaskManagerService
    {
        private static readonly List<TaskItem> _tasks = new List<TaskItem>();
        private static readonly string _storageFile = "data/tasks.json";

        static TaskManagerService()
        {
            LoadTasks();
        }

        public static List<TaskItem> GetAllTasks()
        {
            return _tasks.ToList();
        }

        public static void AddTask(TaskItem task)
        {
            _tasks.Add(task);
            SaveTasks();
        }

        public static bool RemoveTask(Guid id)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                _tasks.Remove(task);
                SaveTasks();
                return true;
            }
            return false;
        }

        public static bool RemoveTaskByTitle(string title)
        {
            var task = _tasks.FirstOrDefault(t => t.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (task != null)
            {
                _tasks.Remove(task);
                SaveTasks();
                return true;
            }
            return false;
        }

        private static void SaveTasks()
        {
            try
            {
                var directory = Path.GetDirectoryName(_storageFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_tasks, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                File.WriteAllText(_storageFile, json);
                Log.Debug("[STORAGE] Задачи сохранены в файл: {File}", _storageFile);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[STORAGE] Ошибка при сохранении задач");
            }
        }

        private static void LoadTasks()
        {
            try
            {
                if (File.Exists(_storageFile))
                {
                    var json = File.ReadAllText(_storageFile);
                    var tasks = JsonSerializer.Deserialize<List<TaskItem>>(json);
                    if (tasks != null)
                    {
                        _tasks.Clear();
                        _tasks.AddRange(tasks);
                        Log.Information("[STORAGE] Загружено {Count} задач из файла", _tasks.Count);
                    }
                }
                else
                {
                    Log.Information("[STORAGE] Файл хранилища не найден. Создана новая коллекция.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[STORAGE] Ошибка при загрузке задач");
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            GlobalExceptionHandler.Subscribe();

            InitializeLogging();

            try
            {
                Log.Information("========================================");
                Log.Information("TaskManager v{Version} запускается", "1.0.0");
                Log.Information("OS: {OS} | User: {User} | CLR: {CLR}",
                    Environment.OSVersion.Platform,
                    Environment.UserName,
                    Environment.Version);
                Log.Information("========================================");

                RunMainMenu();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[FATAL] Критическая ошибка в главном методе");
                Console.WriteLine("\n!!! КРИТИЧЕСКАЯ ОШИБКА !!!");
                Console.WriteLine("Приложение не может продолжить работу.");
            }
            finally
            {
                Log.Information("========================================");
                Log.Information("TaskManager завершает работу");
                Log.Information("========================================");
                Log.CloseAndFlush();
            }
        }

        private static void InitializeLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()

                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information)

                .WriteTo.File(
                    path: "logs/app-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Debug)

                .WriteTo.File(
                    path: "logs/errors-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Error)

                .WriteTo.File(
                    new JsonFormatter(),
                    path: "logs/structured/app-.json",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)

                .WriteTo.File(
                    path: "logs/warnings-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    restrictedToMinimumLevel: LogEventLevel.Warning)

                .CreateLogger();
        }

        private static void RunMainMenu()
        {
            OperationTracer.Start("ApplicationSession", "Главный цикл приложения");

            bool isRunning = true;

            while (isRunning)
            {
                try
                {
                    Console.WriteLine("\n========== TASK MANAGER ==========");
                    Console.WriteLine("1. Добавить задачу");
                    Console.WriteLine("2. Удалить задачу");
                    Console.WriteLine("3. Показать все задачи");
                    Console.WriteLine("4. Статистика");
                    Console.WriteLine("5. Тестирование ошибок");
                    Console.WriteLine("0. Выход");
                    Console.Write("\nВыберите действие: ");

                    string? choice = Console.ReadLine()?.Trim();

                    switch (choice)
                    {
                        case "1":
                            AddTaskMenu();
                            break;
                        case "2":
                            RemoveTaskMenu();
                            break;
                        case "3":
                            ListTasksMenu();
                            break;
                        case "4":
                            ShowStatistics();
                            break;
                        case "5":
                            TestErrorHandling();
                            break;
                        case "0":
                            Log.Information("Пользователь выбрал выход из приложения");
                            isRunning = false;
                            break;
                        default:
                            Log.Warning("Неверный выбор меню: {Choice}", choice ?? "(пусто)");
                            Console.WriteLine("Неверный выбор. Попробуйте снова.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MAIN] Ошибка в главном меню");
                    Console.WriteLine("\n!!! ОШИБКА !!!");
                    Console.WriteLine("Произошла непредвиденная ошибка. Подробности в логе.");
                }
            }

            OperationTracer.Stop("ApplicationSession", "Success", "Сеанс завершен пользователем");
        }

        private static void AddTaskMenu()
        {
            OperationTracer.Start("AddTask", "Меню добавления задачи");

            try
            {
                Console.WriteLine("\n--- Добавление новой задачи ---");

                Console.Write("Название задачи: ");
                string? title = Console.ReadLine()?.Trim();

                if (string.IsNullOrWhiteSpace(title))
                {
                    Log.Warning("[VALIDATION] Пустое название задачи");
                    OperationTracer.Stop("AddTask", "Warning", "Отменено: пустое название");
                    Console.WriteLine("Ошибка: название не может быть пустым!");
                    return;
                }

                if (title.Length > 100)
                {
                    Log.Warning("[VALIDATION] Название слишком длинное: {Length} символов", title.Length);
                    OperationTracer.Stop("AddTask", "Warning", "Отменено: название > 100 символов");
                    Console.WriteLine("Ошибка: название не может превышать 100 символов!");
                    return;
                }

                Console.Write("Описание (необязательно): ");
                string? description = Console.ReadLine()?.Trim() ?? "";

                Console.Write("Приоритет (1-Low, 2-Medium, 3-High, 4-Critical) [2]: ");
                string? priorityInput = Console.ReadLine()?.Trim();

                PriorityLevel priority = PriorityLevel.Medium;
                if (!string.IsNullOrEmpty(priorityInput) && int.TryParse(priorityInput, out int p))
                {
                    priority = (PriorityLevel)Math.Clamp(p, 1, 4);
                }

                var task = new TaskItem(title, description, priority);
                TaskManagerService.AddTask(task);

                Log.Information("[SUCCESS] Добавлена задача: {@Task}", new
                {
                    task.Id,
                    task.Title,
                    Priority = task.Priority.ToString(),
                    task.Status
                });

                OperationTracer.Stop("AddTask", "Success", $"Задача \"{title}\" добавлена с приоритетом {priority}");
                Console.WriteLine($"\n✓ Задача добавлена!");
                Console.WriteLine($"  ID: {task.Id}");
                Console.WriteLine($"  Название: {task.Title}");
                Console.WriteLine($"  Приоритет: {task.Priority}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEPTION] Ошибка при добавлении задачи");
                OperationTracer.StopWithError("AddTask", ex, "Добавление задачи");
                Console.WriteLine("\n!!! ОШИБКА !!!");
                Console.WriteLine("Не удалось добавить задачу. Подробности в логе.");
            }
        }

        private static void RemoveTaskMenu()
        {
            OperationTracer.Start("RemoveTask", "Меню удаления задачи");

            try
            {
                Console.WriteLine("\n--- Удаление задачи ---");

                var tasks = TaskManagerService.GetAllTasks();
                if (tasks.Count == 0)
                {
                    Log.Warning("[REMOVE] Попытка удаления из пустого списка");
                    OperationTracer.Stop("RemoveTask", "Warning", "Список задач пуст");
                    Console.WriteLine("Список задач пуст. Нечего удалять.");
                    return;
                }

                Console.Write("Введите ID задачи для удаления: ");
                string? input = Console.ReadLine()?.Trim();

                if (string.IsNullOrWhiteSpace(input))
                {
                    Log.Warning("[VALIDATION] Пустой ввод ID");
                    OperationTracer.Stop("RemoveTask", "Warning", "Отменено: пустой ввод");
                    Console.WriteLine("Ошибка: введите ID задачи!");
                    return;
                }

                if (!Guid.TryParse(input, out Guid taskId))
                {
                    Log.Warning("[VALIDATION] Неверный формат ID: {Input}", input);
                    OperationTracer.Stop("RemoveTask", "Warning", $"Неверный ID: {input}");
                    Console.WriteLine("Ошибка: неверный формат ID!");
                    return;
                }

                bool removed = TaskManagerService.RemoveTask(taskId);

                if (removed)
                {
                    Log.Information("[SUCCESS] Удалена задача: {TaskId}", taskId);
                    OperationTracer.Stop("RemoveTask", "Success", $"Задача {taskId} удалена");
                    Console.WriteLine($"✓ Задача {taskId} удалена!");
                }
                else
                {
                    Log.Error("[NOT_FOUND] Задача не найдена: {TaskId}", taskId);
                    OperationTracer.Stop("RemoveTask", "Error", $"Задача {taskId} не найдена");
                    Console.WriteLine($"!!! Задача с ID {taskId} не найдена!");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEPTION] Ошибка при удалении задачи");
                OperationTracer.StopWithError("RemoveTask", ex, "Удаление задачи");
                Console.WriteLine("\n!!! ОШИБКА !!!");
                Console.WriteLine("Не удалось удалить задачу. Подробности в логе.");
            }
        }

        private static void ListTasksMenu()
        {
            OperationTracer.Start("ListTasks", "Меню просмотра задач");

            try
            {
                Console.WriteLine("\n--- Список задач ---");

                var tasks = TaskManagerService.GetAllTasks();

                if (tasks.Count == 0)
                {
                    Log.Information("[EMPTY] Список задач пуст");
                    OperationTracer.Stop("ListTasks", "Success", "Список пуст");
                    Console.WriteLine("Список задач пуст.");
                    return;
                }

                Log.Information("[SUCCESS] Запрошен список задач. Всего: {Count}", tasks.Count);

                Console.WriteLine($"Всего задач: {tasks.Count}\n");

                foreach (var task in tasks)
                {
                    Console.WriteLine($"[{task.Id}]");
                    Console.WriteLine($"  Название: {task.Title}");
                    Console.WriteLine($"  Описание: {(string.IsNullOrEmpty(task.Description) ? "-" : task.Description)}");
                    Console.WriteLine($"  Приоритет: {task.Priority}");
                    Console.WriteLine($"  Статус: {task.Status}");
                    Console.WriteLine($"  Создана: {task.CreatedAt:dd.MM.yyyy HH:mm}");
                    Console.WriteLine();
                }

                OperationTracer.Stop("ListTasks", "Success", $"Выведено {tasks.Count} задач");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEPTION] Ошибка при выводе списка задач");
                OperationTracer.StopWithError("ListTasks", ex, "Вывод списка задач");
                Console.WriteLine("\n!!! ОШИБКА !!!");
                Console.WriteLine("Не удалось показать задачи. Подробности в логе.");
            }
        }

        private static void ShowStatistics()
        {
            OperationTracer.Start("Statistics", "Показ статистики");

            try
            {
                var tasks = TaskManagerService.GetAllTasks();

                Console.WriteLine("\n--- Статистика ---");
                Console.WriteLine($"Всего задач: {tasks.Count}");
                Console.WriteLine($"По приоритетам:");
                Console.WriteLine($"  Critical: {tasks.Count(t => t.Priority == PriorityLevel.Critical)}");
                Console.WriteLine($"  High: {tasks.Count(t => t.Priority == PriorityLevel.High)}");
                Console.WriteLine($"  Medium: {tasks.Count(t => t.Priority == PriorityLevel.Medium)}");
                Console.WriteLine($"  Low: {tasks.Count(t => t.Priority == PriorityLevel.Low)}");
                Console.WriteLine($"\nПо статусам:");
                Console.WriteLine($"  Pending: {tasks.Count(t => t.Status == TaskStatus.Pending)}");
                Console.WriteLine($"  InProgress: {tasks.Count(t => t.Status == TaskStatus.InProgress)}");
                Console.WriteLine($"  Completed: {tasks.Count(t => t.Status == TaskStatus.Completed)}");

                Log.Information("[STATS] Показана статистика: Total={Total}, Critical={Critical}, High={High}",
                    tasks.Count,
                    tasks.Count(t => t.Priority == PriorityLevel.Critical),
                    tasks.Count(t => t.Priority == PriorityLevel.High));

                OperationTracer.Stop("Statistics", "Success", "Статистика показана");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEPTION] Ошибка при показе статистики");
                OperationTracer.StopWithError("Statistics", ex, "Показ статистики");
            }
        }

        private static void TestErrorHandling()
        {
            OperationTracer.Start("TestError", "Тестирование обработки ошибок");

            try
            {
                Console.WriteLine("\n--- Тестирование обработки ошибок ---");
                Console.WriteLine("1. Вызвать деление на ноль");
                Console.WriteLine("2. Вызвать NullReferenceException");
                Console.WriteLine("3. Вызвать IndexOutOfRangeException");
                Console.Write("Выберите тест (1-3): ");

                string? choice = Console.ReadLine()?.Trim();

                switch (choice)
                {
                    case "1":
                        Log.Warning("[TEST] Тест: деление на ноль");
                        int a = 10;
                        int b = 0;
                        int result = a / b; 
                        break;
                    case "2":
                        Log.Warning("[TEST] Тест: NullReferenceException");
                        string? nullString = null;
                        Console.WriteLine(nullString!.Length);
                        break;
                    case "3":
                        Log.Warning("[TEST] Тест: IndexOutOfRangeException");
                        int[] arr = new int[3];
                        Console.WriteLine(arr[10]);
                        break;
                    default:
                        Log.Warning("[TEST] Неверный выбор теста");
                        Console.WriteLine("Неверный выбор!");
                        OperationTracer.Stop("TestError", "Warning", "Неверный выбор теста");
                        return;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TEST] Перехвачено тестовое исключение типа {ExceptionType}", ex.GetType().Name);
                OperationTracer.StopWithError("TestError", ex, "Тестирование исключений");
                Console.WriteLine("\n!!! ОШИБКА ПЕРЕХВАЧЕНА !!!");
                Console.WriteLine($"Тип: {ex.GetType().Name}");
                Console.WriteLine($"Сообщение: {ex.Message}");
                Console.WriteLine("Подробности в логе ошибок.");
            }
        }
    }
}