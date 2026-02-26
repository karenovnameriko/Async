using System.Threading.Tasks;
namespace KT1_async;

public class ArrayProcessor
{
    public void IncrementArrayElements(int[] array)
    {
        Parallel.For(0, array.Length, i =>
        {
            array[i]++;
        });
    }

    public void PrintArray(int[] array)
    {
        foreach (var item in array)
        {
            Console.Write(item + " ");
        }
        Console.WriteLine();
    }
}