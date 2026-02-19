using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;
using Serilog.Events;

namespace TaskManager
{
    public class TaskItem
    {
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid Id { get; set; }

        public TaskItem(string title)
        {
            Id = Guid.NewGuid();
            Title = title ?? throw new ArgumentNullException(nameof(title));
            CreatedAt = DateTime.Now;
        }
    }

    public static class OperationTracer
    {
        private static readonly Dictionary<string, Stopwatch> _stopwatches = new Dictionary<string, Stopwatch>();

        public static void StartOperation(string operationName)
        {
            var stopwatch = Stopwatch.StartNew();
            _stopwatches[operationName] = stopwatch;
            Log.Debug("[TRACE] Начало операции: {Operation}", operationName);
        }

        public static void EndOperation(string operationName, string result, string details = "")
        {
            if (_stopwatches.TryGetValue(operationName, out var stopwatch))
            {
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds;

                if (result == "Success")
                {
                    Log.Information("[TRACE] Завершение операции: {Operation}. Результат: {Result}. Время: {ElapsedMs} мс. {Details}",
                        operationName, result, elapsedMs, details);
                }
                else if (result == "Error")
                {
                    Log.Error("[TRACE] Завершение операции: {Operation}. Результат: {Result}. Время: {ElapsedMs} мс. {Details}",
                        operationName, result, elapsedMs, details);
                }
                else if (result == "Fatal")
                {
                    Log.Fatal("[TRACE] Завершение операции: {Operation}. Результат: {Result}. Время: {ElapsedMs} мс. {Details}",
                        operationName, result, elapsedMs, details);
                }
                else
                {
                    Log.Warning("[TRACE] Завершение операции: {Operation}. Результат: {Result}. Время: {ElapsedMs} мс. {Details}",
                        operationName, result, elapsedMs, details);
                }

                _stopwatches.Remove(operationName);
            }
        }

        public static void EndOperationWithError(string operationName, Exception ex, string context)
        {
            if (_stopwatches.TryGetValue(operationName, out var stopwatch))
            {
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds;

                Log.Error(ex,
                    "[EXCEPTION] Ошибка в операции: {Operation}. Контекст: {Context}. Время: {ElapsedMs} мс. Сообщение: {Message}. Стек: {StackTrace}",
                    operationName, context, elapsedMs, ex.Message, ex.StackTrace);

                _stopwatches.Remove(operationName);
            }
            else
            {
                Log.Error(ex,
                    "[EXCEPTION] Ошибка в операции: {Operation}. Контекст: {Context}. Сообщение: {Message}. Стек: {StackTrace}",
                    operationName, context, ex.Message, ex.StackTrace);
            }
        }
    }

    public static class ExceptionHandler
    {
        public static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            var ex = args.ExceptionObject as Exception;
            Log.Fatal(ex, "[FATAL] Необработанное исключение в приложении. IsTerminating: {IsTerminating}", args.IsTerminating);
            Console.WriteLine("\n!!! КРИТИЧЕСКАЯ ОШИБКА !!!");
            Console.WriteLine("Приложение будет завершено. Подробности в логе.");
        }

        public static void HandleTaskSchedulerException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            Log.Error(args.Exception, "[FATAL] Необработанное исключение в Task");
            args.SetObserved();
        }
    }

    class Program
    {
        static List<TaskItem> tasks = new List<TaskItem>();
        static bool isRunning = true;

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += ExceptionHandler.HandleUnhandledException;
            TaskScheduler.UnobservedTaskException += ExceptionHandler.HandleTaskSchedulerException;

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.File(
                    path: "logs\\taskmanager-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.File(
                    path: "logs\\errors\\errors-.log",
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: LogEventLevel.Error,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.File(
                    path: "logs\\structured\\taskmanager-.json",
                    rollingInterval: RollingInterval.Day,
                    formatter: new Serilog.Formatting.Json.JsonFormatter()
                )
                .CreateLogger();

            try
            {
                Log.Information("Программа TaskManager запущена. Версия: {Version}, User: {User}",
                    "1.0.0", Environment.UserName);

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
                        case "test-error":
                            TestErrorHandling();
                            break;
                        default:
                            if (!string.IsNullOrEmpty(command))
                            {
                                Log.Warning("Неизвестная команда: {Command}", command);
                                Console.WriteLine("Неизвестная команда. Доступные: add, remove, list, exit, test-error");
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[FATAL] Критическая ошибка в главном цикле приложения");
                Console.WriteLine("\n!!! КРИТИЧЕСКАЯ ОШИБКА !!!");
                Console.WriteLine("Подробности в логе ошибок.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        static void AddTask()
        {
            OperationTracer.StartOperation("AddTask");

            try
            {
                Console.Write("Введите название задачи: ");
                string? title = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(title))
                {
                    Log.Warning("[VALIDATION] Попытка добавления пустой задачи. Пользователь: {User}", Environment.UserName);
                    OperationTracer.EndOperation("AddTask", "Warning", "Пустое название задачи");
                    Console.WriteLine("Ошибка: название задачи не может быть пустым");
                    return;
                }

                if (title.Length > 100)
                {
                    Log.Warning("[VALIDATION] Название задачи слишком длинное: {Length} символов", title.Length);
                    OperationTracer.EndOperation("AddTask", "Warning", "Название слишком длинное");
                    Console.WriteLine("Ошибка: название задачи не может превышать 100 символов");
                    return;
                }

                var task = new TaskItem(title);
                tasks.Add(task);

                Log.Information("[SUCCESS] Задача добавлена: {TaskId} - {TaskTitle}. Всего задач: {TaskCount}",
                    task.Id, task.Title, tasks.Count);
                OperationTracer.EndOperation("AddTask", "Success", $"Задача \"{title}\" добавлена");
                Console.WriteLine($"Задача \"{title}\" добавлена (ID: {task.Id})");
            }
            catch (ArgumentNullException ex)
            {
                Log.Error(ex, "[EXCEPTION] ArgumentNullException при добавлении задачи");
                OperationTracer.EndOperationWithError("AddTask", ex, "Создание объекта TaskItem");
                Console.WriteLine("\n!!! ОШИБКА !!!");
                Console.WriteLine("Не удалось создать задачу. Подробности в логе.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEPTION] Неожиданная ошибка при добавлении задачи");
                OperationTracer.EndOperationWithError("AddTask", ex, "Добавление задачи");
                Console.WriteLine("\n!!! ОШИБКА !!!");
                Console.WriteLine("Произошла ошибка при добавлении задачи.");
            }
        }

        static void RemoveTask()
        {
            OperationTracer.StartOperation("RemoveTask");

            try
            {
                Console.Write("Введите название задачи для удаления: ");
                string? title = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(title))
                {
                    Log.Warning("[VALIDATION] Попытка удаления с пустым названием");
                    OperationTracer.EndOperation("RemoveTask", "Warning", "Пустое название задачи");
                    Console.WriteLine("Ошибка: название задачи не может быть пустым");
                    return;
                }

                var taskToRemove = tasks.FirstOrDefault(t => t.Title == title);

                if (taskToRemove != null)
                {
                    tasks.Remove(taskToRemove);
                    Log.Information("[SUCCESS] Задача удалена: {TaskId} - {TaskTitle}. Осталось: {TaskCount}",
                        taskToRemove.Id, taskToRemove.Title, tasks.Count);
                    OperationTracer.EndOperation("RemoveTask", "Success", $"Задача \"{title}\" удалена");
                    Console.WriteLine($"Задача \"{title}\" удалена");
                }
                else
                {
                    Log.Error("[NOT_FOUND] Задача не найдена для удаления: {TaskTitle}. Текущее количество: {TaskCount}",
                        title, tasks.Count);
                    OperationTracer.EndOperation("RemoveTask", "Error", $"Задача \"{title}\" не найдена");
                    Console.WriteLine("\n!!! ОШИБКА !!!");
                    Console.WriteLine($"Задача \"{title}\" не найдена в списке.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEPTION] Ошибка при удалении задачи");
                OperationTracer.EndOperationWithError("RemoveTask", ex, "Удаление задачи");
                Console.WriteLine("\n!!! ОШИБКА !!!");
                Console.WriteLine("Произошла ошибка при удалении задачи.");
            }
        }

        static void ListTasks()
        {
            OperationTracer.StartOperation("ListTasks");

            try
            {
                if (tasks.Count == 0)
                {
                    Log.Information("[EMPTY] Список задач пуст");
                    OperationTracer.EndOperation("ListTasks", "Success", "Список пуст");
                    Console.WriteLine("Список задач пуст");
                    return;
                }

                Log.Information("[SUCCESS] Запрошен список задач. Всего: {Count}", tasks.Count);
                Console.WriteLine($"\nСписок задач ({tasks.Count}):");

                for (int i = 0; i < tasks.Count; i++)
                {
                    var task = tasks[i];
                    Console.WriteLine($"{i + 1}. [{task.Id}] {task.Title} (создана: {task.CreatedAt:dd.MM.yyyy HH:mm})");
                }

                OperationTracer.EndOperation("ListTasks", "Success", $"Выведено {tasks.Count} задач");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEPTION] Ошибка при выводе списка задач");
                OperationTracer.EndOperationWithError("ListTasks", ex, "Вывод списка задач");
                Console.WriteLine("\n!!! ОШИБКА !!!");
                Console.WriteLine("Произошла ошибка при выводе списка задач.");
            }
        }

        static void ExitApp()
        {
            OperationTracer.StartOperation("ExitApp");
            Log.Information("[EXIT] Завершение работы. Всего задач: {TotalTasks}", tasks.Count);
            Log.Information("[EXIT] Программа завершена корректно");
            OperationTracer.EndOperation("ExitApp", "Success", "Программа завершена корректно");
            isRunning = false;
        }

        static void TestErrorHandling()
        {
            OperationTracer.StartOperation("TestError");

            try
            {
                Log.Warning("[TEST] Тестирование обработки исключений");
                Console.WriteLine("Тестирование обработки исключений...");

                throw new InvalidOperationException("Это тестовое исключение для проверки логирования ошибок");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TEST] Перехвачено тестовое исключение. Тип: {ExceptionType}", ex.GetType().Name);
                OperationTracer.EndOperationWithError("TestError", ex, "Тестирование обработки исключений");
                Console.WriteLine("\n!!! ОШИБКА !!!");
                Console.WriteLine($"Тип исключения: {ex.GetType().Name}");
                Console.WriteLine($"Сообщение: {ex.Message}");
                Console.WriteLine("Подробности в логе ошибок.");
            }
        }
    }
}