using System.Net;
using System.Threading.Tasks;
namespace KT1_async;

public class DownloadManager
{
    public async Task DownloadFilesAsync(string[] urls)
    {
        var tasks = new Task[urls.Length];
        for (int i = 0; i < urls.Length; i++)
        {
            tasks[i] = DownloadFileAsync(urls[i]);
        }
        await Task.WhenAll(tasks);
    }
    private async Task DownloadFileAsync(string url)
    {
        using (var client = new WebClient())
        {
            // Здесь код для скачивания файла
            try
            {
                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = Guid.NewGuid().ToString();
                }
                await client.DownloadFileTaskAsync(url, fileName);
                Console.WriteLine($"Скачан файл: {fileName}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Ошибка при скачивании {url}: {e.Message}");
            }
        }
    }

}