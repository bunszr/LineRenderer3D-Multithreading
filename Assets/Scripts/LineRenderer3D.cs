using UnityEngine;
using System.Collections.Generic;

public class LineRenderer3D : MonoBehaviour
{
    public MultiTubeGenerator tubeGenerator;
    public Transform pointAdderT;
    Vector3 lastAddedPoint;

    List<PointInfo> points = new List<PointInfo>();

    private void Start()
    {
        lastAddedPoint = pointAdderT.localPosition;
        tubeGenerator.Init(GetComponent<MeshFilter>().mesh = new Mesh(), points);
    }

    private void Update()
    {
        if (Vector3.Distance(lastAddedPoint, pointAdderT.localPosition) > .15f)
        {
            points.Add(new PointInfo { point = pointAdderT.localPosition, radius = 1 });
            lastAddedPoint = pointAdderT.localPosition;
            if (points.Count > 2)
            {
                tubeGenerator.GenerateMesh();
            }
        }
    }

    private void OnDestroy()
    {
        tubeGenerator.Dispose();
    }
}