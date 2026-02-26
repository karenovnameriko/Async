using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace KT1_async;

public class DataProcessor
{
    public void ProcessData(ConcurrentBag<int> data)
    {
        Parallel.ForEach(data, item =>
        {
            // Обработка элемента
            Console.WriteLine($"Обработка элемента: {item}, Поток: {Task.CurrentId}");
        });
    }
}
