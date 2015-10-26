using UnityEngine;
using System.Collections;

public class GridPoint03 {

    public float DepthX, DepthY;
    public Vector3 CameraPosition;
    public int DepthIndex;
    public int VertexID;

    public GridPoint03(int depthX, int depthY, int depthIndex)
    {
        DepthX = depthX;
        DepthY = depthY;
        DepthIndex = depthIndex;
        VertexID = -1;
    }

    public void Reset()
    {
        CameraPosition = Vector3.zero;
        VertexID = -1;
    }

    public bool IsVertex()
    {
        return VertexID >= 0;
    }

}
