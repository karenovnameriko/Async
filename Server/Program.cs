
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

internal class Program
{
    private const string PipeName = "RequestProcessingPipe";

    // Один общий семафор для всех клиентов этого сервера.
    private static readonly SemaphoreSlim ProcessingSemaphore = new(2, 2);

    private static int _activeRequests;
    private static int _clientCounter;

    private static async Task Main()
    {
        using var shutdown = new CancellationTokenSource();
        var clientTasks = new List<Task>();

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;

            if (!shutdown.IsCancellationRequested)
            {
                Console.WriteLine(
                    "\n[SERVER] Получен Ctrl+C. Прекращаем принимать подключения.");

                shutdown.Cancel();
            }
        };

        Console.CancelKeyPress += cancelHandler;

        Console.WriteLine("[SERVER] Сервер запущен.");
        Console.WriteLine($"[SERVER] Имя канала: {PipeName}");
        Console.WriteLine("[SERVER] Максимум одновременных обработок: 2");
        Console.WriteLine("[SERVER] Нажми Ctrl+C для остановки.\n");

        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    await pipe.WaitForConnectionAsync(shutdown.Token);
                }
                catch (OperationCanceledException)
                    when (shutdown.IsCancellationRequested)
                {
                    await pipe.DisposeAsync();
                    break;
                }
                catch
                {
                    await pipe.DisposeAsync();
                    throw;
                }

                // Не принимаем нового клиента, если остановка уже запрошена.
                if (shutdown.IsCancellationRequested)
                {
                    await pipe.DisposeAsync();
                    break;
                }

                int clientId = Interlocked.Increment(ref _clientCounter);

                Console.WriteLine($"[SERVER] Клиент #{clientId} подключился.");

                // Не ждём завершения одного клиента перед подключением следующего.
                clientTasks.Add(
                    HandleClientAsync(pipe, clientId, shutdown.Token));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVER] Ошибка: {ex.Message}");
        }
        finally
        {
            // Больше не читаем новые заявки от подключённых клиентов.
            shutdown.Cancel();
            Console.CancelKeyPress -= cancelHandler;

            try
            {
                // Каждый обработчик клиента дожидается своих заявок.
                // Поэтому сервер не завершает работу раньше этих обработок.
                await Task.WhenAll(clientTasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[SERVER] Ошибка при завершении клиентов: {ex.Message}");
            }
            finally
            {
                ProcessingSemaphore.Dispose();

                Console.WriteLine(
                    "\n[SERVER] Завершение сервера окончено.");
            }
        }
    }

    private static async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        int clientId,
        CancellationToken shutdownToken)
    {
        await using var ownedPipe = pipe;

        using var reader = new StreamReader(ownedPipe, leaveOpen: true);
        using var writer = new StreamWriter(ownedPipe, leaveOpen: true)
        {
            AutoFlush = true
        };

        using var writeLock = new SemaphoreSlim(1, 1);
        var requestTasks = new List<Task>();

        try
        {
            while (!shutdownToken.IsCancellationRequested)
            {
                string? line;

                try
                {
                    line = await reader.ReadLineAsync(shutdownToken);
                }
                catch (OperationCanceledException)
                    when (shutdownToken.IsCancellationRequested)
                {
                    break;
                }

                // Клиент закрыл соединение.
                if (line is null)
                {
                    break;
                }

                RequestMessage? request;

                try
                {
                    request = JsonSerializer.Deserialize<RequestMessage>(line);
                }
                catch (JsonException)
                {
                    Console.WriteLine(
                        $"[SERVER] Клиент #{clientId}: некорректный JSON.");
                    continue;
                }

                if (request is null ||
                    string.IsNullOrWhiteSpace(request.Id) ||
                    string.IsNullOrWhiteSpace(request.Payload))
                {
                    Console.WriteLine(
                        $"[SERVER] Клиент #{clientId}: некорректная заявка.");
                    continue;
                }

                // Запускаем обработку и сразу продолжаем читать следующие заявки.
                requestTasks.Add(
                    ProcessRequestAsync(request, clientId, writer, writeLock));
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine(
                $"[SERVER] Соединение с клиентом #{clientId} прервано: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            // Соединение было закрыто при завершении.
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[SERVER] Ошибка клиента #{clientId}: {ex.Message}");
        }
        finally
        {
            try
            {
                // Writer и writeLock остаются доступны, пока заявки не закончатся.
                await Task.WhenAll(requestTasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[SERVER] Ошибка заявок клиента #{clientId}: {ex.Message}");
            }

            Console.WriteLine(
                $"[SERVER] Обслуживание клиента #{clientId} завершено.");
        }
    }

    private static async Task ProcessRequestAsync(
        RequestMessage request,
        int clientId,
        StreamWriter writer,
        SemaphoreSlim writeLock)
    {
        bool acquired = false;
        bool started = false;
        var stopwatch = new Stopwatch();

        try
        {
            // Если уже обрабатываются две заявки, ждём освобождения места.
            await ProcessingSemaphore.WaitAsync();
            acquired = true;

            int active = Interlocked.Increment(ref _activeRequests);

            stopwatch.Start();
            started = true;

            Console.WriteLine(
                $"[START] ID={request.Id}; клиент=#{clientId}; " +
                $"активных обработок={active}; заявка=\"{request.Payload}\"");

            int delay = Random.Shared.Next(500, 1501);

            await Task.Delay(delay);

            var response = new ResponseMessage(
                request.Id,
                $"Заявка обработана: {request.Payload}",
                delay);

            string json = JsonSerializer.Serialize(response);

            // Несколько обработок могут завершиться почти одновременно.
            // Защищаем запись, чтобы ответы не смешивались.
            await writeLock.WaitAsync();

            try
            {
                await writer.WriteLineAsync(json);
            }
            finally
            {
                writeLock.Release();
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine(
                $"[ERROR] ID={request.Id}; ошибка отправки ответа: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine(
                $"[ERROR] ID={request.Id}; соединение уже закрыто.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ERROR] ID={request.Id}; ошибка обработки: {ex.Message}");
        }
        finally
        {
            if (acquired)
            {
                if (started)
                {
                    stopwatch.Stop();

                    int active = Interlocked.Decrement(ref _activeRequests);

                    Console.WriteLine(
                        $"[END] ID={request.Id}; клиент=#{clientId}; " +
                        $"время={stopwatch.ElapsedMilliseconds} мс; " +
                        $"активных обработок={active}");
                }

                // Выполняется и при ошибке обработки или отправки ответа.
                ProcessingSemaphore.Release();
            }
        }
    }
}

internal sealed record RequestMessage(string Id, string Payload);

internal sealed record ResponseMessage(
    string Id,
    string Result,
    int ProcessingMilliseconds);
