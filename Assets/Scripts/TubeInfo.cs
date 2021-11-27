using UnityEngine;

[CreateAssetMenu(fileName = "TubeInfo")]
public class TubeInfo : ScriptableObject
{
    public CapType capType;
    public int CapCount = 6;
    public int pipeSegmentCount = 5;
    public float capDst = .5f;
}