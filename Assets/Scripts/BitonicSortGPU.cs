using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Runtime.InteropServices;
using System;

// By Olli S.
// My Bitonic sort implementation, with some helper methods to test the validity of the results.
// It can't process more than 2097152 values at the moment.

// Motivation for this:
// I wanted to learn how the Bitonic sort actually works,
// and I also wanted to implement it on GPU (at least partially.)
// In this implementation the outer loop that does multiple dispatches is on the C# side.
// I wanted also write it as simple as possibe, so that it would be understandable later.
// So no bitwise operations etc. like in those very performant Microsoft examples...
// This was made purely for learning purposes, there's not much point in this otherwise.

// Analysis helper method verifies the results so that the sorted sequence:
// - Has all the numbers of the range (such as 0-100.)
// - The numbers are in correct order.
// - No duplicate numbers.
// - No missing numbers.
// - Large number sequence will not print to debug as it will just freeze Unity.

public class BitonicSortGPU : MonoBehaviour
{

    public ComputeShader compute;

    // The well known test data for bitonic sort.
    // Comment out LCGRange below if you want to use this instead of an arbitrary length sequence.
    uint[] testSequence = { 3, 7, 4, 8, 6, 2, 1, 5 };


    void Start()
    {
        SortSequence();
    }


    void SortSequence()
    {
        // Generate desired length test sequence. My debug print won't output over 1024 numbers.
        uint count = 2097152;
        print("<b>Generating a shuffled sequence, count: " + count + "</b>\n");

        float startTime = Time.realtimeSinceStartup;
        uint[] data = GenerateRandomSequence(count);
        // uint[] data = testSequence;
        float elapsedTime = Time.realtimeSinceStartup - startTime;

        Debug.Log("Sequence generated.");
        Debug.Log("Time used: " + elapsedTime);
        print("<b>Shuffled data: </b>\n");
        PrintSequence(data);

        Debug.Log("<b>Performing a normal List Sort for comparison.</b>");
        List<uint> dataList = data.ToList();
        startTime = Time.realtimeSinceStartup;
        dataList.Sort();
        elapsedTime = Time.realtimeSinceStartup - startTime;
        Debug.Log("Time used: " + elapsedTime);

        // The actual data size.
        int DATA_SIZE = data.Length;

        // Reserve the next power of two size for the sort.
        int BUFFER_SIZE = GetNextPowerOfTwo(data.Length);

        // Init buffer.
        var buffer = new ComputeBuffer(BUFFER_SIZE, Marshal.SizeOf(typeof(uint)));

        // Add padding to the data.
        data = AddPow2Padding(data, BUFFER_SIZE);

        // Perform Bitonic sort.
        Debug.Log("<b>Performing bitonic sort on GPU.</b>");

        startTime = Time.realtimeSinceStartup;
        BitonicSort(data, buffer, DATA_SIZE);
        elapsedTime = Time.realtimeSinceStartup - startTime;

        Debug.Log("Sort performed.");
        Debug.Log("Time used: " + elapsedTime);

        // Sorted data is in Data structured buffer.
        data = ReadDataFromGPU(buffer);

        // Release buffer.
        buffer.Release();

        // Trim padding data away. It's in the end of the array.
        data = TrimData(data, DATA_SIZE);

        print("<b>Sorted data: </b>\n");
        PrintSequence(data);

        // Perform analysis if sort was a success.
        AnalyzeTestData(data);
    }


    // Reads data back from a buffer.
    uint[] ReadDataFromGPU(ComputeBuffer buffer)
    {
        var data = new uint[buffer.count];
        buffer.GetData(data);
        return data;
    }


    // Bitonic sort, outer loops. Innermost loop is performed in compute shader.
    void BitonicSort(uint[] data, ComputeBuffer buffer, int size)
    {
        // Find the kernel.
        int kernel_sort = compute.FindKernel("BitonicSort");

        // Set the data to the buffer.
        buffer.SetData(data);

        // Set buffer to the compute program.
        compute.SetBuffer(kernel_sort, "Data", buffer);

        // Number of items is the actual item number, not padded buffer count.
        int NumItems = size;
        int arraySizePow2 = buffer.count;

        // Number of thread groups to dispatch.
        int threadGroupsX = Mathf.Max(1, arraySizePow2 / 8);

        int count = 1;

        for (int k = 2; k < NumItems * 2; k *= 2)
        {
            for (int j = k / 2; j > 0; j /= 2)
            {
                compute.SetInt("_ArraySize", NumItems);
                compute.SetInt("_SubarraySize", k);
                compute.SetInt("_CompareDistance", j);

                // Perform inner loop in GPU
                compute.Dispatch(kernel_sort, threadGroupsX, 1, 1);

                count++;
            }
        }
        Debug.Log("Performed " + count + " loops.");
    }


    // Get the next power of two number.
    int GetNextPowerOfTwo(int x)
    {
        return (int)Mathf.Pow(2, Mathf.Ceil(Mathf.Log(x) / Mathf.Log(2)));
    }


    // Adds padding to the array to make it match the pow2 size.
    // A huge value is used as a filler. This will be removed in the end.
    uint[] AddPow2Padding(uint[] data, int size)
    {
        List<uint> dataPow2 = data.ToList();
        while (dataPow2.Count < size)
        {
            dataPow2.Add(100000000);
        }
        return dataPow2.ToArray();
    }


    // Removes padding from data.
    uint[] TrimData(uint[] data, int size)
    {
        uint[] trimmedData = new uint[size];
        Array.Copy(data, trimmedData, size);
        return trimmedData;
    }


    // Method for printing out analysis of the bitonic sequence sort result.
    // I made this for testing the results when building this.
    void AnalyzeTestData(uint[] data)
    {
        Debug.Log("<b>Analyzing data:</b>");

        Debug.Log("Data length: " + data.Length);

        var (lowest, highest) = LowestAndHighest(data);
        Debug.Log("Lowest number: " + lowest);
        Debug.Log("Highest number: " + highest);

        if (IsAscending(data))
        {
            Debug.Log("<color=green>Sequence is ascending.</color>");
        }
        else
        {
            Debug.Log("<color=red>Sequence is not ascending!</color>");
        }

        var (hasAllNumbers, inRangeCount) = HasAllNumbersOfRange(data, lowest, highest);
        if (hasAllNumbers)
        {
            Debug.Log("<color=green>Sequence has " + inRangeCount + " unique elements, matches data length.</color>");
        }
        else
        {
            Debug.Log("<color=red>Sequence has " + inRangeCount + " unique elements, does not match data length!</color>");
        }

        if (HasDuplicates(data))
        {
            Debug.Log("<color=red>Data contains duplicates.</color>");
            int duplicateCount = CountDuplicates(data);
            Debug.Log("Count of numbers that have duplicates: " + duplicateCount);
            PrintDuplicates(data);
        }
        else
        {
            Debug.Log("<color=green>No duplicates found in data.</color>");
        }
    }


    // Returns the lowest and highest value in a test sequence.
    (uint lowest, uint highest) LowestAndHighest(uint[] data)
    {
        uint lowest = (uint)data.Length;
        uint highest = 0;

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] <= lowest) lowest = data[i];
            if (data[i] >= highest) highest = data[i];
        }

        return (lowest, highest);
    }


    // Tests is a sequence is correctly sorted to be ascending.
    bool IsAscending(uint[] data)
    {
        bool isAscending = true;

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] > data[Mathf.Min(i + 1, data.Length - 1)])
                isAscending = false;
        }

        if (isAscending)
        {
            return true;
        }
        else
        {
            return false;
        }
    }


    // Tests if a sequence has all the numbers in a range (such as every value between 0-100.)
    (bool, int) HasAllNumbersOfRange(uint[] data, uint lowest, uint highest)
    {
        int inRangeCount = 0;
        HashSet<uint> dupes = new HashSet<uint>();

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] >= lowest && data[i] <= highest)
            {
                if (!dupes.Contains(data[i]))
                {
                    dupes.Add(data[i]);
                    inRangeCount++;
                }
            }
        }

        if (inRangeCount == data.Length)
        {
            return (true, inRangeCount);
        }
        else
        {
            return (false, inRangeCount);
        }
    }


    // Tests if a sequence has any duplicate values.
    bool HasDuplicates(uint[] data)
    {
        if (!data.All(new HashSet<uint>().Add))
        {
            return true;
        }
        else
        {
            return false;
        }
    }


    // Counts duplicates if there's any and returns the count-
    // Otherwise returns -1.
    int CountDuplicates(uint[] data)
    {
        int duplicateCount = 0;

        if (!data.All(new HashSet<uint>().Add))
        {
            foreach (var number in data.GroupBy(x => x))
            {
                if (number.Count() - 1 > 0)
                {
                    duplicateCount++;
                }
            }
            return duplicateCount;
        }
        else
        {
            return -1;
        }
    }


    // Prints the duplicate values.
    void PrintDuplicates(uint[] data)
    {
        if (!data.All(new HashSet<uint>().Add))
        {
            foreach (var number in data.GroupBy(x => x))
            {
                if (number.Count() - 1 > 0)
                {
                    Debug.Log(number.Key + " repeats " + (number.Count() - 1) + " times.");
                }
            }
        }
    }


    // Prints a compact number sequence.
    void PrintSequence<T>(T[] data)
    {
        if (data.Length > 1024)
        {
            Debug.Log("<color=red>Too much data to print.</color>");
            return;
        }
        string s = "";
        for (int i = 0; i < data.Length; i++)
        {
            s += (data[i] + " ");
        }
        Debug.Log(s);
    }


    // Linear Congruential Pseudo-random Number Generator
    uint[] GenerateRandomSequence(uint length)
    {
        if (length == 0)
        {
            length = 4;
        }
        if (length > 2097152)
        {
            Debug.Log("<color=red>Too long sequence, clamped to 2097152.</color>");
            length = 2097152;
        }

        uint maximum = length - 1;
        uint value = (uint)UnityEngine.Random.Range(0, maximum);
        uint offset = (uint)UnityEngine.Random.Range(0, maximum) * 2 + 1;
        uint seed = 4 * (maximum / 4) + 1;
        uint modulus = (uint)Mathf.Pow(2, Mathf.Ceil(Mathf.Log(maximum, 2)));

        uint[] sequence = new uint[maximum + 1];

        uint count = 0;

        while (count <= maximum)
        {
            if (value <= maximum)
            {
                sequence[count] = value + 1;
                count += 1;
            }
            value = (value * seed + offset) % modulus;
        }

        return sequence;
    }

}