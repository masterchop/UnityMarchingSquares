using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Windows.Kinect;


public class KinectMapper04 : MonoBehaviour {
    
    KinectSensor sensor;
    MultiSourceFrameReader reader;

    // PUBLIC:
    public float DepthMinimum = 0.6f;
    public float DepthMaximum = 4.0f;

    // PRIVATE:    
    ushort[] depthData;
    byte[] colorData;
    byte[] bodyIndexData;

    int depthWidth, depthHeight; 
    int colorWidth;

    uint colorBytesPerPixel, colorLengthInBytes;

    CoordinateMapper coordinateMapper;
    ColorSpacePoint[] colorSpacePoints;
    CameraSpacePoint[] cameraSpacePoints;

    byte[] mappedColorData;

    Texture2D texture;
    
    List<GridPoint04> gridPoints;

    Mesh mesh;
    List<Vector3> vertices;
    List<int> triangles;
    List<Vector2> uvs;

    private int gridWidth;
    private int gridHeight;
    public int gridStep = 10;

    void Start () {

        sensor = KinectSensor.GetDefault();

        if ( sensor != null )
        {
            reader = sensor.OpenMultiSourceFrameReader(
                FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.BodyIndex);

            coordinateMapper = sensor.CoordinateMapper;

            FrameDescription depthFrameDesc = sensor.DepthFrameSource.FrameDescription;
            depthData = new ushort[depthFrameDesc.LengthInPixels];
            depthWidth = depthFrameDesc.Width;
            depthHeight = depthFrameDesc.Height;
            
            FrameDescription colorFrameDesc = sensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Rgba);
            colorLengthInBytes = colorFrameDesc.LengthInPixels * colorFrameDesc.BytesPerPixel;
            colorData = new byte[colorLengthInBytes];
            colorWidth = colorFrameDesc.Width;
            colorBytesPerPixel = colorFrameDesc.BytesPerPixel;

            FrameDescription bodyIndexDesc = sensor.BodyIndexFrameSource.FrameDescription;
            bodyIndexData = new byte[bodyIndexDesc.LengthInPixels * bodyIndexDesc.BytesPerPixel];

            // PREPARE THE COLOR TO DEPTH MAPPED BYTE ARRAY FOR CREATING OUR DYNAMIC TEXTURE
            // ---------------------------------------------------------------------------------------

            // STEP 1. ALLOCATE SPACE FOR THE RESULT OF MAPPING COLORSPACEPOINTS FOR EACH DEPTH FRAME POINT
            colorSpacePoints = new ColorSpacePoint[depthFrameDesc.LengthInPixels];
            cameraSpacePoints = new CameraSpacePoint[depthFrameDesc.LengthInPixels];

            // STEP 2. PREPARE THE BYTE ARRAY THAT WILL HOLD A CALCULATED COLOR PIXEL FOR EACH DEPTH PIXEL
            //         THIS BYTE ARRAY WILL BE FED TO THE MATERIAL AND USED AS THE MESH TEXTURE
            mappedColorData = new byte[depthFrameDesc.LengthInPixels * colorFrameDesc.BytesPerPixel];

            // STEP 3. CREATE A TEXTURE THAT HAS THE SIZE OF THE DEPTH FRAME BUT CAN HOLD RGBA VALUES FROM THE COLOR FRAME
            texture = new Texture2D(depthFrameDesc.Width, depthFrameDesc.Height, TextureFormat.RGBA32, false);

            // STEP 4. BIND THE MAIN TEXTURE TO THE LOCAL VARIABLE FOR FUTURE PROCESSING
            gameObject.GetComponent<Renderer>().material.mainTexture = texture;
            
            if (!sensor.IsOpen) sensor.Open();

            BuildTestingGrid();
            
        } else
        {
            Debug.LogError("Couldn't find Kinect Sensor!");
        }

    }

    void BuildTestingGrid()
    {
        gameObject.GetComponent<MeshFilter>().mesh = mesh = new Mesh();
        mesh.name = "Variable Mesh";

        vertices = new List<Vector3>();
        triangles = new List<int>();
        uvs = new List<Vector2>();

        gridPoints = new List<GridPoint04>();

        for (int y = 0; y < depthHeight; y += gridStep)
        {
            for (int x = 0; x < depthWidth; x += gridStep)
            {
                int depthIndex = x + (y * depthWidth);
                GridPoint04 gp = new GridPoint04(x, y, depthIndex);
                gp.Reset();
                gridPoints.Add(gp);
            }
        }
        gridWidth = (int)Mathf.Floor(depthWidth / gridStep);
        gridHeight = (int)Mathf.Floor(depthHeight / gridStep);
    }

    void GenerateMeshPerFrame()
    {
        mesh.Clear();

        vertices.Clear();
        triangles.Clear();
        uvs.Clear();

        int halfDepthWidth = depthWidth / 2;
        int halfDepthHeight = depthHeight / 2;

        // FIRST RESET ANY EXISTING GRID VERTEX DATA
        foreach ( GridPoint04 gp in gridPoints)
        {
            gp.Reset();
        }

        GridPoint04[] testingSquare = new GridPoint04[4];

        int armTest1, headTest1, armTest2, headTest2;

        // NOW REEVALUATE THE GRID, CALCULATING EACH SQUARE TO FIND ACCEPTABLE TRIANGLES
        for (int gridIndex = 0, y = 0; y < gridHeight; y++, gridIndex++)
        {
            for (int x = 0; x < gridWidth; x++, gridIndex++)
            {

                GridPoint04 a = testingSquare[0] = gridPoints[gridIndex];
                GridPoint04 b = testingSquare[1] = gridPoints[gridIndex + 1];
                GridPoint04 c = testingSquare[2] = gridPoints[gridIndex + gridWidth + 1];
                GridPoint04 d = testingSquare[3] = gridPoints[gridIndex + gridWidth + 2];

                // EVALUATE THE TESTING SQUARE FOR GridPoints THAT SHOULD BE CONVERTED TO VERTICES
                foreach (GridPoint04 gp in testingSquare)
                {
                    // IF THE GRIDPOINT IS IN THE BODY SILHOUETTE...
                    if (bodyIndexData[gp.DepthIndex] != 255)
                    {
                        if ( !gp.IsVertex())
                        {
                            // CALUCATE THE WORLD POSITION
                            CameraSpacePoint csp = cameraSpacePoints[gp.DepthIndex];
                            gp.CameraPosition = new Vector3(csp.X, csp.Y, csp.Z);

                            // IF THE CAMERA POSITION IS VALID
                            if (!float.IsInfinity(gp.CameraPosition.x) &&
                                !float.IsInfinity(gp.CameraPosition.y) &&
                                !float.IsInfinity(gp.CameraPosition.z))
                            {
                                // AND IT LIES WITHIN THE USER DEFINED DEPTH RANGE
                                if (gp.CameraPosition.z > DepthMinimum && gp.CameraPosition.z < DepthMaximum)
                                {
                                    // ADD IT TO OUR VERTEX COLLECTION AND RECORD IT'S VERTEX INDEX
                                    gp.VertexID = vertices.Count;
                                    vertices.Add(gp.CameraPosition);
                                    uvs.Add(new Vector2(gp.DepthX / (float)depthWidth, gp.DepthY / (float)depthHeight));
                                }
                            }
                        }
                    }
                }

                // CALCULATE THE BIT KEY FOR ALL ACCEPTABLE VERTICES IN OUR TESTING SQUARE
                int SquareKey = 0;

                if (a.IsVertex()) SquareKey |= 1;
                if (b.IsVertex()) SquareKey |= 2;
                if (c.IsVertex()) SquareKey |= 4;
                if (d.IsVertex()) SquareKey |= 8;


                switch (SquareKey)
                {
                    case 0:
                        // NO VERTICES WERE FOUND. DO NOTHING.
                        break;

                    case 1:
                        // CUT TRIANGLE - ONLY A IS GOOD, FIND A ARM AND A HEAD
                        armTest1 = getArmVertex(a);
                        headTest1 = getHeadVertex(a);
                        if ((armTest1 < 0) || (headTest1 < 0)) continue;
                        addTriangle(a.VertexID, armTest1, headTest1);
                        break;

                    case 2:
                        // CUT TRIANGLE - ONLY B IS GOOD, FIND A ARM AND B HEAD
                        armTest1 = getArmVertex(a);
                        headTest1 = getHeadVertex(b);
                        if ((armTest1 < 0) || (headTest1 < 0)) continue;
                        addTriangle(armTest1, b.VertexID, headTest1);
                        break;

                    case 3:
                        // CUT QUAD - ONLY A AND B ARE GOOD, FIND A HEAD AND B HEAD
                        headTest1 = getHeadVertex(a);
                        headTest2 = getHeadVertex(b);
                        if ((headTest1 < 0) || (headTest2 < 0)) continue;

                        // BOTTOM TRIANGLE
                        addTriangle(a.VertexID, b.VertexID, headTest1);
                        // TOP TRIANGLE
                        addTriangle(b.VertexID, headTest2, headTest1);
                        break;

                    case 4:
                        // CUT TRIANGLE - ONLY C IS GOOD, FIND A HEAD AND C ARM
                        armTest1 = getArmVertex(c);
                        headTest1 = getHeadVertex(a);
                        if ((armTest1 < 0) || (headTest1 < 0)) continue;
                        addTriangle(headTest1, armTest1, c.VertexID);
                        break;

                    case 5:
                        // CUT QUAD - ONLY A AND C ARE GOOD, FIND A ARM AND C ARM
                        armTest1 = getArmVertex(a);
                        armTest2 = getArmVertex(c);
                        if ((armTest1 < 0) || (armTest2 < 0)) continue;

                        // LEFT TRIANGLE
                        addTriangle(a.VertexID, armTest1, c.VertexID);
                        // RIGHT TRIANGLE
                        addTriangle(armTest1, armTest2, c.VertexID);
                        break;


                    case 6:
                        // 6 AND 9 ARE AMBIGUOUS AS OPPOSITE CORNERS C AND B ARE HOT.
                        // THEY MAY REPRESENT A DIAGNONAL STRIP ACROSS THE GRID OR TWO INDEPENDENT 
                        // CUT TRIANGLES AT THE CORNERS. WE NEED TO CHECK THE GRID
                        // CENTER TO DETERMINE WHICH SOLUTION TO ASSUME
                        armTest1 = getArmVertex(a);
                        headTest1 = getHeadVertex(a);
                        armTest2 = getArmVertex(c);
                        headTest2 = getHeadVertex(b);

                        if ((armTest1 < 0) || (headTest1 < 0) || (armTest2 < 0) || (headTest2 < 0 ) ) continue;

                        if ( isGridCenterInBody(a) )
                        {
                            // ADD CORNER CUT TRIANGLES AND DIAGONAL PARTIAL QUAD FOR CENTER
                            addTriangle(headTest1, armTest2, c.VertexID);
                            addTriangle(armTest1, armTest2, headTest1);
                            addTriangle(armTest1, headTest2, armTest2);
                            addTriangle(armTest1, b.VertexID, headTest2);
                        } else
                        {
                            // ONLY ADD CORNER CUT TRIANGLES
                            addTriangle(headTest1, armTest2, c.VertexID);
                            addTriangle(armTest1, b.VertexID, headTest2);
                        }
                        break;


                    case 7:
                        // DIAMOND - A, B, C ARE GOOD SO MARCH TOWARD D
                        armTest1 = getArmVertex(c);
                        headTest1 = getHeadVertex(b);
                        if ((armTest1 < 0) || (headTest1 < 0)) continue;

                        addTriangle(a.VertexID, armTest1, c.VertexID);
                        addTriangle(a.VertexID, headTest1, armTest1);
                        addTriangle(a.VertexID, b.VertexID, headTest1);
                        break;

                    case 8:
                        // CUT TRIANGLE - ONLY D IS GOOD, FIND C ARM AND B HEAD
                        armTest1 = getArmVertex(c);
                        headTest1 = getHeadVertex(b);
                        if ((armTest1 < 0) || (headTest1 < 0)) continue;
                        addTriangle(headTest1, d.VertexID, armTest1);
                        break;


                    case 9:
                        // 6 AND 9 ARE AMBIGUOUS AS OPPOSITE CORNERS A AND D ARE HOT.
                        // THEY MAY REPRESENT A DIAGNONAL STRIP ACROSS THE GRID OR TWO INDEPENDENT 
                        // CUT TRIANGLES AT THE CORNERS. WE NEED TO CHECK THE GRID
                        // CENTER TO DETERMINE WHICH SOLUTION TO ASSUME
                        armTest1 = getArmVertex(a);
                        headTest1 = getHeadVertex(a);
                        armTest2 = getArmVertex(c);
                        headTest2 = getHeadVertex(b);

                        if ((armTest1 < 0) || (headTest1 < 0) || (armTest2 < 0) || (headTest2 < 0)) continue;

                        if (isGridCenterInBody(a))
                        {
                            // ADD CORNER CUT TRIANGLES AND DIAGONAL PARTIAL QUAD FOR CENTER
                            addTriangle(a.VertexID, armTest1, headTest1);
                            addTriangle(headTest1, armTest1, armTest2);
                            addTriangle(headTest2, armTest2, armTest1);
                            addTriangle(armTest2, headTest2, d.VertexID);
                        }
                        else
                        {
                            // ONLY ADD CORNER CUT TRIANGLES
                            addTriangle(a.VertexID, armTest1, headTest1);
                            addTriangle(armTest2, headTest2, d.VertexID);
                        }
                        break;

                    case 10:
                        // CUT QUAD - ONLY B AND D ARE GOOD, FIND A ARM AND C ARM
                        armTest1 = getArmVertex(a);
                        armTest2 = getArmVertex(c);
                        if ((armTest1 < 0) || (armTest2 < 0)) continue;

                        // LEFT TRIANGLE
                        addTriangle(armTest1, d.VertexID, armTest2);
                        // RIGHT TRIANGLE
                        addTriangle(armTest1, b.VertexID, d.VertexID);
                        break;

                    case 11:
                        // DIAMOND - A, B, D ARE GOOD SO MARCH TOWARD C
                        armTest1 = getArmVertex(c);
                        headTest1 = getHeadVertex(a);
                        if ((armTest1 < 0) || (headTest1 < 0)) continue;
                        addTriangle(b.VertexID, headTest1, a.VertexID);
                        addTriangle(b.VertexID, armTest1, headTest1);
                        addTriangle(b.VertexID, d.VertexID, armTest1);
                        break;

                    case 12:
                        // CUT QUAD - ONLY C AND D ARE GOOD, FIND A HEAD AND B HEAD
                        headTest1 = getHeadVertex(a);
                        headTest2 = getHeadVertex(b);
                        if ((headTest1 < 0) || (headTest2 < 0)) continue;

                        // BOTTOM TRIANGLE
                        addTriangle(headTest1, headTest2, c.VertexID);
                        // TOP TRIANGLE
                        addTriangle(headTest2, d.VertexID, c.VertexID);
                        break;

                    case 13:
                        // DIAMOND - A, C, D ARE GOOD SO MARCH TOWARD B
                        armTest1 = getArmVertex(a);
                        headTest1 = getHeadVertex(b);
                        if ((armTest1 < 0) || (headTest1 < 0)) continue;
                        addTriangle(c.VertexID, a.VertexID, armTest1);
                        addTriangle(c.VertexID, armTest1, headTest1);
                        addTriangle(c.VertexID, headTest1, d.VertexID);
                        break;

                    case 14:
                        // DIAMOND - B, C, D ARE GOOD SO MARCH TOWARD A
                        armTest1 = getArmVertex(a);
                        headTest1 = getHeadVertex(a);
                        if ((armTest1 < 0) || (headTest1 < 0)) continue;

                        addTriangle(d.VertexID, c.VertexID, headTest1);
                        addTriangle(d.VertexID, headTest1, armTest1);
                        addTriangle(d.VertexID, armTest1, b.VertexID);
                        break;

                    case 15:
                        // FULL QUAD - ALL GRID POINTS ARE VALID
                        addTriangle(a.VertexID, b.VertexID, c.VertexID);
                        addTriangle(b.VertexID, d.VertexID, c.VertexID);
                        break;
                        
                }
            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();

        mesh.RecalculateNormals();
    }

    void addTriangle(int index0, int index1, int index2)
    {
        triangles.Add(index0);
        triangles.Add(index1);
        triangles.Add(index2);
    }

    bool isGridCenterInBody(GridPoint04 gp)
    {
        int centerDepthIndex = gp.VertexID + (gridStep / 2) + ((gridStep / 2) * depthWidth);
        return bodyIndexData[centerDepthIndex] != 255;
    }

    int getHeadVertex(GridPoint04 gp)
    {
        if (gp.isHeadVertex())
        {
            return gp.headVertexID;
        }

        bool seekingSilhouette = true;
        if (gp.IsVertex()) seekingSilhouette = false;

        int startingIndex = gp.DepthIndex;
        
        for (int i = 0; i <= gridStep; i++)
        {
            // WE ARE MOVING UP THE DEPTH IMAGE, SO OUR INDEX JUMPS A ROW EACH TIME
            bool isInBody = (bodyIndexData[startingIndex] != 255);

            if (isInBody && seekingSilhouette) {
                CameraSpacePoint csp = cameraSpacePoints[startingIndex];

                if (!float.IsInfinity(csp.X) &&
                    !float.IsInfinity(csp.Y) &&
                    !float.IsInfinity(csp.Z))
                {

                    gp.headVertexID = vertices.Count;
                    vertices.Add(new Vector3(csp.X, csp.Y, csp.Z));

                    uvs.Add(new Vector2(gp.DepthX / (float)depthWidth, (gp.DepthY + i) / (float)depthHeight));

                    return gp.headVertexID;
                }
            }

            if ( !isInBody && !seekingSilhouette)
            {
                // IF WE WERE IN THE BODY AND MOVING OUTWARD, AT THIS POINT WE HAVE PASSED 
                // THE EDGE SO WE WANT TO MOVE BACK ONE TO THE LAST VALID VALUE.
                CameraSpacePoint csp = cameraSpacePoints[startingIndex - depthWidth];

                if (float.IsInfinity(csp.X) ||
                    float.IsInfinity(csp.Y) ||
                    float.IsInfinity(csp.Z))
                {
                    continue;
                }

                gp.headVertexID = vertices.Count;
                vertices.Add(new Vector3(csp.X, csp.Y, csp.Z));

                uvs.Add(new Vector2(gp.DepthX / (float)depthWidth, ( gp.DepthY + (i-1)) / (float)depthHeight));

                return gp.headVertexID;
            }

            startingIndex += depthWidth;

        }

        if (gp.IsVertex())
        {
            return gp.VertexID;
        }
        else
        {
            return -1;
        }
    }


    int getArmVertex(GridPoint04 gp)
    {
        // IF THE ARM HAS BEEN MARCHED BY A PREVIOUS TESTING SQUARE,
        // IT WILL ALREADY HAVE A VERTEX ID SO JUST RETURN IT.
        // OTHERWISE, WE WILL NEED TO MARCH THE ARM AND CREATE A NEW VERTEX
        if (gp.isArmVertex())
        {
            return gp.armVertexID;
        }

        // WE ASSUME THE GRID POINT IS OUTSIDE THE SILHOUETTE AND LOOKING FOR IT
        bool seekingSilhouette = true;

        // BUT WE MAY BE STARTING INSIDE THE SILHOUETTE AND LOOKING FOR THE EDGE
        // SO TEST FOR THAT BY CHECKING IF THE CORE IS A VERTEX, WHICH WOULD MEAN IT WAS VALID
        if (gp.IsVertex())
        {
            seekingSilhouette = false;
        }

        int startingIndex = gp.DepthIndex;

        for (int i = 0; i <= gridStep; i++)
        {
            bool isInBody = (bodyIndexData[startingIndex + i] != 255);

            if (isInBody && seekingSilhouette)
            {
                // IF WE WERE IN THE BODY AND MOVING OUTWARD, AT THIS POINT WE HAVE PASSED 
                // THE EDGE SO WE WANT TO MOVE BACK ONE TO THE LAST VALID VALUE.
                CameraSpacePoint csp = cameraSpacePoints[startingIndex + i];

                if (float.IsInfinity(csp.X) ||
                    float.IsInfinity(csp.Y) ||
                    float.IsInfinity(csp.Z))
                {
                    continue;
                }

                gp.armVertexID = vertices.Count;
                vertices.Add(new Vector3(csp.X, csp.Y, csp.Z));

                uvs.Add(new Vector2((gp.DepthX + i) / (float)depthWidth, gp.DepthY / (float)depthHeight));

                return gp.armVertexID;

            }
            if (!isInBody && !seekingSilhouette)
            {
                // IF WE WERE IN THE BODY AND MOVING OUTWARD, AT THIS POINT WE HAVE PASSED 
                // THE EDGE SO WE WANT TO MOVE BACK ONE TO THE LAST VALID VALUE.
                CameraSpacePoint csp = cameraSpacePoints[startingIndex + (i-1)];

                if (float.IsInfinity(csp.X) ||
                    float.IsInfinity(csp.Y) ||
                    float.IsInfinity(csp.Z))
                {
                    continue;
                }

                gp.armVertexID = vertices.Count;
                vertices.Add(new Vector3(csp.X, csp.Y, csp.Z));

                uvs.Add(new Vector2( (gp.DepthX + (i-1)) / (float)depthWidth, gp.DepthY / (float)depthHeight));

                return gp.armVertexID;
            }
        }

        if ( gp.IsVertex() )
        {
            return gp.VertexID;
        } else
        {
            return -1;
        }
    }

    void Update () {

        bool updateFrameMapping = true;

        if ( reader != null )
        {
            MultiSourceFrame frame = reader.AcquireLatestFrame();

            if ( frame != null )
            {

                // GRAB DEPTH DATA
                using (DepthFrame depthFrame = frame.DepthFrameReference.AcquireFrame())
                {
                    if (depthFrame != null)
                    {
                        depthFrame.CopyFrameDataToArray(depthData);
                    }
                    else
                    {
                        updateFrameMapping = false;
                    }
                }

                // GRAB COLOR DATA
                using (ColorFrame colorFrame = frame.ColorFrameReference.AcquireFrame())
                {
                    if ( colorFrame != null )
                    {
                        colorFrame.CopyConvertedFrameDataToArray(colorData, ColorImageFormat.Rgba);
                    } else
                    {
                        updateFrameMapping = false;
                    }
                }

                // GRAB THE BODY INDEX FRAME DATA
                using (BodyIndexFrame bodyIndexFrame = frame.BodyIndexFrameReference.AcquireFrame())
                {
                    if (bodyIndexFrame != null)
                    {
                        bodyIndexFrame.CopyFrameDataToArray(bodyIndexData);
                    }
                    else
                    {
                        updateFrameMapping = false;
                    }
                }

                // ATTEMPT TO CREATE THE CALCULATED TEXTURE MAP
                if ( updateFrameMapping )
                {
                    coordinateMapper.MapDepthFrameToColorSpace(depthData, colorSpacePoints);
                    coordinateMapper.MapDepthFrameToCameraSpace(depthData, cameraSpacePoints);

                    for (
                        int mappedColorIndex = 0, depthIndex = 0;
                        depthIndex < depthData.Length;
                        depthIndex++, mappedColorIndex += 4)
                    {
                        int colorFrameIndex = returnColorFrameIndex(colorSpacePoints[depthIndex]);

                        if (colorFrameIndex >= 0 && colorFrameIndex < (colorData.Length - (int)colorBytesPerPixel))
                        {
                            // we have a successful mapping of the depth coordinate into the color frame data
                            copyColorPixelToMappedPixel(mappedColorIndex, colorFrameIndex);

                        }
                        else
                        {
                            // mapped location falls out of color frame range, so clear the pixel instead.
                            clearMappedPixel(mappedColorIndex);
                        }

                    } // end for each pixel calculations

                    texture.LoadRawTextureData(mappedColorData);
                    texture.Apply();

                    GenerateMeshPerFrame();

                } // end if updateFrameMapping


            }// end if frame
        } // end if sensor
	
	}

    private void clearMappedPixel(int index)
    {
        mappedColorData[index] = 0;         // R
        mappedColorData[index + 1] = 0;     // G
        mappedColorData[index + 2] = 0;     // B
        mappedColorData[index + 3] = 0;     // A
    }

    private int returnColorFrameIndex(ColorSpacePoint csp)
    {
        int colorX = (int)(Mathf.Floor(csp.X + 0.5f));
        int colorY = (int)(Mathf.Floor(csp.Y + 0.5f));
        int colorIndex = (int)colorBytesPerPixel * (colorX + (colorY * colorWidth));

        return colorIndex;
    }

    private void copyColorPixelToMappedPixel(int mappedIndex, int colorIndex)
    {
        mappedColorData[mappedIndex] = colorData[colorIndex];             // R
        mappedColorData[mappedIndex + 1] = colorData[colorIndex + 1];     // G
        mappedColorData[mappedIndex + 2] = colorData[colorIndex + 2];     // B
        mappedColorData[mappedIndex + 3] = colorData[colorIndex + 3];     // A
    }

    void OnApplicationQuit()
    {
        if (reader != null)
        {
            reader.Dispose();
            reader = null;
        }

        if (sensor != null)
        {
            if (sensor.IsOpen)
            {
                sensor.Close();
            }

            sensor = null;
        }
    }

}
