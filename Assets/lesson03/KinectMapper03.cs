using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Windows.Kinect;


public class KinectMapper03 : MonoBehaviour {
    
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
    
    List<GridPoint03> gridPoints;

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

        gridPoints = new List<GridPoint03>();

        for (int y = 0; y < depthHeight; y += gridStep)
        {
            for (int x = 0; x < depthWidth; x += gridStep)
            {
                int depthIndex = x + (y * depthWidth);
                GridPoint03 gp = new GridPoint03(x, y, depthIndex);
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
        foreach ( GridPoint03 gp in gridPoints)
        {
            gp.Reset();
        }

        GridPoint03[] testingSquare = new GridPoint03[4];

        // NOW REEVALUATE THE GRID, CALCULATING EACH SQUARE TO FIND ACCEPTABLE TRIANGLES
        for (int gridIndex = 0, y = 0; y < gridHeight; y++, gridIndex++)
        {
            for (int x = 0; x < gridWidth; x++, gridIndex++)
            {

                GridPoint03 a = testingSquare[0] = gridPoints[gridIndex];
                GridPoint03 b = testingSquare[1] = gridPoints[gridIndex + 1];
                GridPoint03 c = testingSquare[2] = gridPoints[gridIndex + gridWidth + 1];
                GridPoint03 d = testingSquare[3] = gridPoints[gridIndex + gridWidth + 2];

                // EVALUATE THE TESTING SQUARE FOR GridPoints THAT SHOULD BE CONVERTED TO VERTICES
                foreach ( GridPoint03 gp in testingSquare)
                {
                    // IF THE GRIDPOINT IS IN THE BODY SILHOUETTE...
                    if (bodyIndexData[gp.DepthIndex] != 255)
                    {
                        // CALUCATE THE WORLD POSITION
                        CameraSpacePoint csp = cameraSpacePoints[gp.DepthIndex];
                        gp.CameraPosition = new Vector3(csp.X, csp.Y, csp.Z);

                        if (float.IsInfinity(gp.CameraPosition.x) ||
                             float.IsInfinity(gp.CameraPosition.y) ||
                            float.IsInfinity(gp.CameraPosition.z))
                        {
                            // THE VERTEX WOULD BE OUT OF BOUNDS SO SKIP
                            continue;
                        }
                        else
                        {
                            // IF IT IS NOT ALREADY A CALCULATED VERTEX FROM A PREVIOUS TESTINGSQUARE
                            if (!gp.IsVertex())
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

                switch ( SquareKey)
                {
                    case 0:
                        // NO VERTICES WERE FOUND. DO NOTHING.
                        break;

                    case 7:
                        // A, B, C ARE GOOD
                        addTriangle(a.VertexID, b.VertexID, c.VertexID);
                        break;

                    case 11:
                        // A, B, D ARE GOOD
                        addTriangle(a.VertexID, b.VertexID, d.VertexID);
                        break;

                    case 13:
                        addTriangle(a.VertexID, d.VertexID, c.VertexID);
                        break;

                    case 14:
                        // B, C, D ARE GOOD
                        addTriangle(b.VertexID, d.VertexID, c.VertexID);
                        break;

                    case 15:
                        // ALL GRID POINTS ARE VALID, BUILD TWO TRIANGLES
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
