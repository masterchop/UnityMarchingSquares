using UnityEngine;
using System.Collections;

public class GridPoint04 {

    public float DepthX, DepthY;
    public Vector3 CameraPosition;
    public int DepthIndex;
    public int VertexID;
    
    public int headVertexID;
    public int armVertexID;

    public GridPoint04(int depthX, int depthY, int depthIndex)
    {
        DepthX = depthX;
        DepthY = depthY;
        DepthIndex = depthIndex;
        VertexID = -1;
    }

    public void Reset()
    {
        CameraPosition = Vector3.zero;
        VertexID = armVertexID = headVertexID = -1;
    }

    public bool IsVertex()
    {
        return VertexID >= 0;
    }

    public bool isArmVertex()
    {
        return armVertexID >= 0;
    }

    public bool isHeadVertex()
    {
        return headVertexID >= 0;
    }

}
