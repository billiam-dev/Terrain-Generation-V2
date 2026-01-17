using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Tracks realtime with static methods.
/// 
/// Usasge:
///     doulbe myTimer;
/// 
///     Stopwatch.Start(ref myTimer);
///     DoStuff();
///     Stopwatch.End(ref myTimer);
///
///     print(myTimer);
///
/// </summary>
public class Stopwatch
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Start(ref double timer)
    {
        timer = Time.realtimeSinceStartupAsDouble;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void End(ref double timer)
    {
        timer = Time.realtimeSinceStartupAsDouble - timer;
    }

    public static double ToMilliseconds(double time)
    {
        double timeMiliseconds = time * 1000.0;

        timeMiliseconds *= 100.0;
        timeMiliseconds = math.round(timeMiliseconds);
        timeMiliseconds /= 100.0;

        return timeMiliseconds;
    }
}
