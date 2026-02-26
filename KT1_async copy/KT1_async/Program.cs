using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using KT1_async;

namespace KT1_asyn;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("ПРИМЕР 1");

        string[] urls =
        {
            "https://example.com",
            "https://www.microsoft.com",
            "https://www.github.com"
        };

        DownloadManager downloadManager = new DownloadManager();
        await downloadManager.DownloadFilesAsync(urls);

        Console.WriteLine("\nПРИМЕР 2");

        int[] array = new int[10];

        for (int i = 0; i < array.Length; i++)
        {
            array[i] = i;
        }

        ArrayProcessor arrayProcessor = new ArrayProcessor();

        Console.WriteLine("До обработки:");
        arrayProcessor.PrintArray(array);

        arrayProcessor.IncrementArrayElements(array);

        Console.WriteLine("После обработки:");
        arrayProcessor.PrintArray(array);

        Console.WriteLine("\nПРИМЕР 3");

        ConcurrentBag<int> data = new ConcurrentBag<int>();

        for (int i = 1; i <= 10; i++)
        {
            data.Add(i);
        }

        DataProcessor dataProcessor = new DataProcessor();
        dataProcessor.ProcessData(data);

        Console.WriteLine("\nВсе примеры выполнены.");
    }
}