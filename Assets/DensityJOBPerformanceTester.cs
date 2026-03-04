using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class DensityJOBPerformanceTester : MonoBehaviour
{
    const int k_BrickSize = 32;
    const int k_BricksPerAxis = 4;

    const int k_InnerloopBatchCount = 32;

    // Total points must equal that of
    // Brick size = 16
    // Bricks per axis = 8

    /*
     * Each Job variant evalues 5 shapes through
     * a polynomal cubic SmoothMin function.
     * 
     * Performance metrics:
     * 
     * IJobFor:
     * - Size = 16, Axis = 8 : ~25.2ms
     * - Size = 32, Axis = 4 : ~22.8ms
     * - Size = 8, Axis = 16 : ~37.5ms
     * 
     * IJobFor w/ parallel scheduling:
     * - Size = 16, Axis = 8 : ~7.1ms
     * - Size = 32, Axis = 4 : ~2.8ms
     * - Size = 8, Axis = 16 : ~35ms
     *
     * IJob parallel scheduling:
     * - Size = 16, Axis = 8 : ~7.8ms
     * - Size = 32, Axis = 4 : ~2.2ms <- best speed
     * - Size = 8, Axis = 16 : ~320ms
    */

    void FixedUpdate()
    {
        double executionTime = Time.realtimeSinceStartupAsDouble;

        //RunTest_IJobFor();
        //RunTest_IJobFor_Parallel();
        RunTest_IJob();

        executionTime = Time.realtimeSinceStartupAsDouble - executionTime;

        int totalJOBs = k_BricksPerAxis * k_BricksPerAxis * k_BricksPerAxis;
        double avarageTime = executionTime / totalJOBs;

        Debug.Log(string.Format("Completed test in {0}, avg: {1}", ToMilliseconds(executionTime), ToMilliseconds(avarageTime)));
    }

    void RunTest_IJobFor()
    {
        int pointsPerBrick = k_BrickSize * k_BrickSize * k_BrickSize;
        NativeArray<float> density = new(pointsPerBrick, Allocator.TempJob);

        int numJobs = k_BricksPerAxis * k_BricksPerAxis * k_BricksPerAxis;
        for (int i = 0; i < numJobs; i++)
        {
            DensityEvaluationIJobFor job = new()
            {
                pointsPerAxis = k_BrickSize,
                density = density
            };

            job.Schedule(pointsPerBrick, default).Complete();
        }

        density.Dispose();
    }

    void RunTest_IJobFor_Parallel()
    {
        int pointsPerBrick = k_BrickSize * k_BrickSize * k_BrickSize;
        NativeArray<float> density = new(pointsPerBrick, Allocator.TempJob);

        int numJobs = k_BricksPerAxis * k_BricksPerAxis * k_BricksPerAxis;
        for (int i = 0; i < numJobs; i++)
        {
            DensityEvaluationIJobFor job = new()
            {
                pointsPerAxis = k_BrickSize,
                density = density
            };

            job.ScheduleParallel(pointsPerBrick, k_InnerloopBatchCount, default).Complete();
        }

        density.Dispose();
    }

    void RunTest_IJob()
    {
        int numJobs = k_BricksPerAxis * k_BricksPerAxis * k_BricksPerAxis;
        NativeArray<float>[] densities = new NativeArray<float>[numJobs];

        int pointsPerBrick = k_BrickSize * k_BrickSize * k_BrickSize;
        for (int i = 0; i < numJobs; i++)
            densities[i] = new(pointsPerBrick, Allocator.TempJob);

        NativeArray<JobHandle> jobs = new(numJobs, Allocator.Temp);
        for (int i = 0; i < numJobs; i++)
        {
            DensityEvaluationIJob job = new()
            {
                pointsPerAxis = k_BrickSize,
                density = densities[i]
            };

            jobs[i] = job.Schedule();
        }

        for (int i = 0; i < numJobs; i++)
            JobHandle.CompleteAll(jobs);

        for (int i = 0; i < numJobs; i++)
            densities[i].Dispose();

        jobs.Dispose();
    }

    static double ToMilliseconds(double time)
    {
        double timeMiliseconds = time * 1000.0;

        timeMiliseconds *= 1000.0;
        timeMiliseconds = math.round(timeMiliseconds);
        timeMiliseconds /= 1000.0;

        return timeMiliseconds;
    }
}

[BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
struct DensityEvaluationIJobFor : IJobFor
{
    [ReadOnly] public int pointsPerAxis;
    [WriteOnly] public NativeArray<float> density;

    public void Execute(int index)
    {
        int z = index / (pointsPerAxis * pointsPerAxis);
        int y = (index / pointsPerAxis) % pointsPerAxis;
        int x = index % pointsPerAxis;

        int3 cellIndex = new(x, y, z);
        float3 worldPosition = (float3)cellIndex;

        float value = 0.0f;
        value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);
        value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);
        value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);
        value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);
        value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);

        density[index] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Sphere(float3 centre, float radius)
    {
        return math.length(centre) - radius;
    }

    const float OneOverSix = 0.16666667f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float SmoothMin(float a, float b, float k)
    {
        float h = math.max(k - math.abs(a - b), 0.0f) / k;
        return math.min(a, b) - h * h * h * k * OneOverSix;
    }
}

[BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true, DisableSafetyChecks = true)]
struct DensityEvaluationIJob : IJob
{
    [ReadOnly] public int pointsPerAxis;
    [WriteOnly] public NativeArray<float> density;

    public void Execute()
    {
        for (int x = 0; x < pointsPerAxis; x++)
        {
            for (int y = 0; y < pointsPerAxis; y++)
            {
                for (int z = 0; z < pointsPerAxis; z++)
                {
                    float3 worldPosition;
                    worldPosition.x = x;
                    worldPosition.y = y;
                    worldPosition.z = z;

                    float value = 0.0f;
                    value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);
                    value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);
                    value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);
                    value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);
                    value = SmoothMin(Sphere(worldPosition, 4.0f), value, 1.0f);

                    int index = (z * pointsPerAxis * pointsPerAxis) + (y * pointsPerAxis) + x;
                    density[index] = value;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Sphere(float3 centre, float radius)
    {
        return math.length(centre) - radius;
    }

    const float OneOverSix = 0.16666667f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float SmoothMin(float a, float b, float k)
    {
        float h = math.max(k - math.abs(a - b), 0.0f) / k;
        return math.min(a, b) - h * h * h * k * OneOverSix;
    }
}
