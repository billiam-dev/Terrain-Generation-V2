using Unity.Mathematics;
using UnityEngine;

namespace TerrainSystem
{
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
        public static void Start(ref double timer) => timer = Time.realtimeSinceStartupAsDouble;

        public static void End(ref double timer) => timer = Time.realtimeSinceStartupAsDouble - timer;

        public static double ToMilliseconds(double time)
        {
            double timeMiliseconds = time * 1000.0;

            timeMiliseconds *= 1000.0;
            timeMiliseconds = math.round(timeMiliseconds);
            timeMiliseconds /= 1000.0;

            return timeMiliseconds;
        }
    }

    public class MeanTime
    {
        readonly double[] times;
        int i;

        public MeanTime()
        {
            times = new double[10];
            i = 0;
        }

        public MeanTime(int arraySize)
        {
            times = new double[arraySize];
            i = 0;
        }

        public void AddTime(double time)
        {
            times[i] = time;

            i++;
            if (i >= times.Length)
                i = 0;
        }

        public double Avarage()
        {
            double t = 0;
            int count = 0;

            for (int i = 0; i < times.Length; i++)
            {
                t += times[i];
                if (t > 0.0)
                    count++;
            }

            return t / count;
        }
    }
}
