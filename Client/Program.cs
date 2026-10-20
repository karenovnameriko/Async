
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

internal class Program
{
    private const string PipeName = "RequestProcessingPipe";
    private const int MinimumRequestCount = 8;

    private static async Task Main()
    {
        Console.Write($"Сколько заявок отправить (минимум {MinimumRequestCount})? ");

        if (!int.TryParse(Console.ReadLine(), out int requestCount) ||
            requestCount < MinimumRequestCount)
        {
            Console.WriteLine(
                $"Ошибка: необходимо отправить не менее {MinimumRequestCount} заявок.");
            return;
        }

        string clientId = Guid.NewGuid().ToString("N");

        Console.WriteLine($"\n[CLIENT] ID клиента: {clientId}");
        Console.WriteLine("[CLIENT] Подключаемся к серверу...");

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(5000);

            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true)
            {
                AutoFlush = true
            };

            Console.WriteLine("[CLIENT] Подключение установлено.\n");

            var sentIds = new HashSet<string>();

            // Сначала отправляем все заявки, не ожидая отдельных ответов.
            for (int i = 1; i <= requestCount; i++)
            {
                string requestId = $"{clientId}-{i:D6}";

                var request = new RequestMessage(
                    requestId,
                    $"Заявка № {i} от клиента {clientId[..8]}");

                string json = JsonSerializer.Serialize(request);

                await writer.WriteLineAsync(json);
                sentIds.Add(requestId);

                Console.WriteLine(
                    $"[SEND] ID={request.Id}; текст=\"{request.Payload}\"");
            }

            Console.WriteLine(
                $"\n[CLIENT] Отправлено заявок: {requestCount}. Ожидаем ответы...\n");

            var receivedIds = new HashSet<string>();

            while (receivedIds.Count < requestCount)
            {
                string? line = await reader.ReadLineAsync();

                if (line is null)
                {
                    Console.WriteLine(
                        "[CLIENT] Соединение закрыто до получения всех ответов.");
                    break;
                }

                ResponseMessage? response;

                try
                {
                    response = JsonSerializer.Deserialize<ResponseMessage>(line);
                }
                catch (JsonException)
                {
                    Console.WriteLine("[CLIENT] Получен некорректный JSON-ответ.");
                    continue;
                }

                if (response is null)
                {
                    Console.WriteLine("[CLIENT] Получен пустой ответ.");
                    continue;
                }

                if (!sentIds.Contains(response.Id))
                {
                    Console.WriteLine(
                        $"[CLIENT] Ответ относится к неизвестной заявке: {response.Id}");
                    continue;
                }

                if (!receivedIds.Add(response.Id))
                {
                    Console.WriteLine(
                        $"[CLIENT] Повторный ответ для заявки {response.Id}");
                    continue;
                }

                Console.WriteLine(
                    $"[RECEIVE] ID={response.Id}; " +
                    $"результат=\"{response.Result}\"; " +
                    $"обработка={response.ProcessingMilliseconds} мс");
            }

            Console.WriteLine(
                $"\n[CLIENT] Получено ответов: {receivedIds.Count} из {requestCount}.");

            if (receivedIds.Count == requestCount)
            {
                Console.WriteLine("[CLIENT] Все заявки получили ответы.");
            }
        }
        catch (TimeoutException)
        {
            Console.WriteLine(
                "[CLIENT] Время подключения истекло. Проверь, запущен ли сервер.");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[CLIENT] Ошибка соединения: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLIENT] Ошибка: {ex.Message}");
        }
    }
}

internal sealed record RequestMessage(string Id, string Payload);

internal sealed record ResponseMessage(
    string Id,
    string Result,
    int ProcessingMilliseconds);
