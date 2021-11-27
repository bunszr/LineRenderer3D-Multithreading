using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

[System.Serializable]
public class MultiTubeGenerator
{
    NativeArray<float3> circlePoints;
    NativeArray<PointInfo> nativePoints;

    Mesh mesh;
    List<PointInfo> points;

    public TubeInfo tubeInfo;

    public void Init(Mesh mesh, List<PointInfo> points)
    {
        this.mesh = mesh;
        this.points = points;
        SetCirclePoints();
    }

    public void GenerateMesh()
    {
        SetNativePointsFromPoints();

        int pointsLength = nativePoints.Length;
        NativeArray<float3> vertices = new NativeArray<float3>(pointsLength * tubeInfo.pipeSegmentCount, Allocator.TempJob);
        NativeArray<float3> normals = new NativeArray<float3>(vertices.Length, Allocator.TempJob);
        NativeArray<float2> uvs = new NativeArray<float2>(vertices.Length, Allocator.TempJob);
        NativeArray<int> triangles = new NativeArray<int>(6 * (tubeInfo.pipeSegmentCount - 1) * (pointsLength - 1), Allocator.TempJob);

        TubeJob tubeJob = new TubeJob
        {
            vertices = vertices,
            normals = normals,
            uvs = uvs,
            triangles = triangles,
            points = nativePoints,
            circlePoints = circlePoints,
            pipeSegmentCount = tubeInfo.pipeSegmentCount,
            pointsLength = pointsLength,
            capType = (int)tubeInfo.capType,
        };
        tubeJob.ScheduleParallel(pointsLength, 1, default).Complete();

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetIndices(triangles, MeshTopology.Triangles, 0);

        nativePoints.Dispose();
        vertices.Dispose();
        triangles.Dispose();
        normals.Dispose();
        uvs.Dispose();
    }

    public void Dispose()
    {
        circlePoints.Dispose();
    }

    void SetNativePointsFromPoints()
    {
        int numPoints = points.Count;

        if (tubeInfo.capType == CapType.Open)
        {
            nativePoints = new NativeArray<PointInfo>(numPoints, Allocator.TempJob);
            CopyPointsToNativePoints(points, 0);
        }
        else if (tubeInfo.capType == CapType.Flat)
        {
            nativePoints = new NativeArray<PointInfo>(numPoints + 2, Allocator.TempJob);
            nativePoints[0] = new PointInfo { point = points[0].point + math.normalizesafe(points[0].point - points[1].point) * .01f, radius = 0 };
            nativePoints[nativePoints.Length - 1] = new PointInfo { point = points[numPoints - 1].point + math.normalizesafe(points[numPoints - 1].point - points[numPoints - 2].point) * .01f, radius = 0 };
            CopyPointsToNativePoints(points, 1);

        }
        else if (tubeInfo.capType == CapType.Capsule)
        {
            nativePoints = new NativeArray<PointInfo>(numPoints + tubeInfo.CapCount * 2, Allocator.TempJob);
            SetFirstCapPoints();
            CopyPointsToNativePoints(points, tubeInfo.CapCount);
            SetEndCapPoints(numPoints);
        }
    }

    void CopyPointsToNativePoints(List<PointInfo> points, int nativeStartIndex)
    {
        for (int i = 0; i < points.Count; i++)
        {
            nativePoints[i + nativeStartIndex] = points[i];
        }
    }

    public void SetCirclePoints()
    {
        circlePoints = new NativeArray<float3>(tubeInfo.pipeSegmentCount, Allocator.Persistent);
        float stepAngle = 2.0f * math.PI / (float)(tubeInfo.pipeSegmentCount - 1);
        float angle = 0.0f;
        for (int i = 0; i < tubeInfo.pipeSegmentCount; i++)
        {
            circlePoints[i] = new float3(math.cos(angle), math.sin(angle), 0);
            angle += stepAngle;
        }
    }

    public void SetFirstCapPoints()
    {
        float3 dir = math.normalizesafe((points[0].point - points[1].point));
        float radius = points[0].radius;
        for (int i = 0; i < tubeInfo.CapCount; i++)
        {
            float time = i / (tubeInfo.CapCount - 1f);
            float easeTime = easeOutQuint(time);
            PointInfo pointInfo = new PointInfo
            {
                point = points[0].point + dir * easeTime * tubeInfo.capDst,
                radius = radius * (1f - time)
            };
            nativePoints[i] = pointInfo;
        }
    }

    public void SetEndCapPoints(int numPoints)
    {
        float3 dir = math.normalizesafe(points[numPoints - 1].point - points[numPoints - 2].point);
        float radius = points[numPoints - 1].radius;
        for (int i = 0; i < tubeInfo.CapCount; i++)
        {
            int index = i + numPoints;
            float time = i / (tubeInfo.CapCount - 1f);
            float easeTime = easeOutQuint(time);
            PointInfo pointInfo = new PointInfo
            {
                point = points[numPoints - 1].point + dir * easeTime * tubeInfo.capDst,
                radius = radius * (1f - time)
            };
            nativePoints[index] = pointInfo;
        }
    }

    float EaseInQuart(float x) => x * x * x * x;
    float easeOutQuint(float x) => 1 - math.pow(1 - x, 5);

    [BurstCompile]
    public struct TubeJob : IJobFor
    {
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<float3> vertices;
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<float3> normals;
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<float2> uvs;
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<int> triangles;

        [ReadOnly] public NativeArray<PointInfo> points;
        [ReadOnly] public NativeArray<float3> circlePoints;
        [ReadOnly] public int pipeSegmentCount, pointsLength, capType;

        public void Execute(int index)
        {
            int startedTriangleIndex = 6 * (pipeSegmentCount - 1) * index;
            int triangleIndex = 0;
            float xUv = index / (pointsLength - 1f);

            PointInfo p = points[index];
            float3 lookDirection = index < pointsLength - 1
                    ? points[index + 1].point - p.point
                    : p.point - points[index - 1].point;
            quaternion lookRotation = quaternion.LookRotationSafe(lookDirection, math.up());

            for (int i = 0; i < pipeSegmentCount; i++)
            {
                int vertIndex = index * pipeSegmentCount + i;
                float3 normal = math.mul(lookRotation, circlePoints[i]);
                vertices[vertIndex] = p.point + normal * p.radius;
                normals[vertIndex] = normal;
                uvs[vertIndex] = new Vector2(xUv, i / (pipeSegmentCount - 1f));

                if (index < pointsLength - 1 && i < pipeSegmentCount - 1)
                {
                    triangles[startedTriangleIndex + triangleIndex + 0] = (index * pipeSegmentCount + i);
                    triangles[startedTriangleIndex + triangleIndex + 1] = (index * pipeSegmentCount + i + 1);
                    triangles[startedTriangleIndex + triangleIndex + 2] = ((index + 1) * pipeSegmentCount + i);

                    triangles[startedTriangleIndex + triangleIndex + 3] = ((index + 1) * pipeSegmentCount + i);
                    triangles[startedTriangleIndex + triangleIndex + 4] = (index * pipeSegmentCount + i + 1);
                    triangles[startedTriangleIndex + triangleIndex + 5] = ((index + 1) * pipeSegmentCount + i + 1);
                    triangleIndex += 6;
                }
            }
        }
    }

}
public struct PointInfo
{
    public float3 point;
    public float radius;
}
public enum CapType { Open, Flat, Capsule }