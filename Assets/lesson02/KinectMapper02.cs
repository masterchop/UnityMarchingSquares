using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Windows.Kinect;

public class KinectMapper02 : MonoBehaviour {
    
    KinectSensor sensor;
    MultiSourceFrameReader reader;

    // PUBLIC:
    public ushort DepthMinimum = 500;
    public ushort DepthMaximum = 2500;
    public bool ShowBodiesOnly = true;

    public float meshDistance = 400;

    public bool isMeshWorldScale = false;
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
            
            BuildSimpleMesh();
            //BuildVariableMesh();

        } else
        {
            Debug.LogError("Couldn't find Kinect Sensor!");
        }

    }

    void BuildSimpleMesh()
    {
        gameObject.GetComponent<MeshFilter>().mesh = mesh = new Mesh();
        mesh.name = "Simple Quad";

        float startingX = -(depthWidth / 2) / 1000.0f;
        float startingY = -(depthHeight / 2) / 1000.0f;
        float quadWidth = depthWidth / 1000.0f;
        float quadHeight = depthHeight / 1000.0f;

        vertices = new List<Vector3>();

        vertices.Add(new Vector3(startingX,             startingY,              0));
        vertices.Add(new Vector3(startingX,             startingY + quadHeight, 0));
        vertices.Add(new Vector3(startingX + quadWidth, startingY,              0));
        vertices.Add(new Vector3(startingX + quadWidth, startingY + quadHeight, 0));

        triangles = new List<int>();
        triangles.Add(0);
        triangles.Add(1);
        triangles.Add(2);

        triangles.Add(2);
        triangles.Add(1);
        triangles.Add(3);

        uvs = new List<Vector2>();

        uvs.Add(new Vector2(0, 0));
        uvs.Add(new Vector2(0, 1));
        uvs.Add(new Vector2(1, 0));
        uvs.Add(new Vector2(1, 1));

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();

        mesh.RecalculateNormals();

    }

    void BuildVariableMesh()
    {
        gameObject.GetComponent<MeshFilter>().mesh = mesh = new Mesh();
        mesh.name = "Variable Mesh";

        vertices = new List<Vector3>();
        triangles = new List<int>();
        uvs = new List<Vector2>();

        // POPULATE THE VERTICES BASED ON THE GRID PARAMETERS
        for (int y = 0; y < depthHeight; y += gridStep)
        {
            for (int x = 0; x < depthWidth; x += gridStep)
            {
                Vector3 newPos = new Vector3(x - (depthWidth / 2), y - (depthHeight / 2), 0);
                vertices.Add(newPos);
                uvs.Add(new Vector2(x / (float)depthWidth, y / (float)depthHeight));
            }
        }

        gridWidth = (int)Mathf.Floor(depthWidth / gridStep);
        gridHeight = (int)Mathf.Floor(depthHeight / gridStep);

        
        // NOW LOOP AND ADD TRIANGLE INDICES
        for (int vertexIndex = 0, y = 0; y < gridHeight; y ++, vertexIndex++)
        {
            for (int x = 0; x < gridWidth; x++, vertexIndex++)
            {
                // TOP TRIANGLE
                triangles.Add(vertexIndex);
                triangles.Add(vertexIndex + 1);
                triangles.Add(vertexIndex + gridWidth + 1);
                //triangles.Add(vertexIndex + 1);
                
                // BOTTOM TRIANGLE
                triangles.Add(vertexIndex + 1 );
                triangles.Add(vertexIndex + gridWidth + 2);
                triangles.Add(vertexIndex + gridWidth + 1);
                //triangles.Add(vertexIndex + gridWidth + 2);

            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();

        mesh.RecalculateNormals();        
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

                        ushort depthValue = depthData[depthIndex];

                        if ( depthValue < DepthMinimum || depthValue > DepthMaximum)
                        {
                            clearMappedPixel(mappedColorIndex);
                            continue;
                        }

                        if ( ShowBodiesOnly && bodyIndexData[depthIndex] == 255 )
                        {
                            // EITHER WE ARE LOOKING ONLY FOR BODIES AND DIDN'T FIND A BODY...
                            clearMappedPixel(mappedColorIndex);
                        }
                        else
                        {
                            // OR WE'RE:
                            // a) LOOKING FOR BODIES AND THIS PIXEL IS IN A BODY SILHOUETTE
                            // b) NOT LOOKING FOR BODIES BUT THIS PIXEL IS IN THE CORRECT DEPTH RANGE
                            // SO EITHER WAY, TRY AND CALCULATE THE COLOR OF THE DEPTH POSTION TO THE COLOR FRAME
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
                        }

                    } // end for each pixel calculations

                    texture.LoadRawTextureData(mappedColorData);
                    texture.Apply();
                    
                    if (isMeshWorldScale)
                    {
                        //UpdateVariableMeshPerFrame();
                    }


                } // end if updateFrameMapping


            }// end if frame
        } // end if sensor
	
	}

    private void UpdateVariableMeshPerFrame()
    {
        // IN THIS CASE, THE UV AND TRIANGLES ARE FINE FOR NOW,
        // SINCE THE "GRID" IS CURRENTLY FIXED AS A [depthWidth x depthHeight] ARRAY OF FIXED COORDINATES
        // SO WE ONLY NEED TO RECALCULATE THE X,Y,Z WORLD VALUES FROM THE MATCHING X,Y PIXELS OF THE DEPTH IMAGE

        mesh.Clear();
        vertices.Clear();

        // POPULATE THE VERTICES BASED ON THE SAME GRID PARAMETERS
        // WE USED WHEN INITIALING CREATING THE MESH AND TRIANGLES SO THE INDICES CONTINUE TO MATCH.

        int halfDepthWidth = depthWidth /2;
        int halfDepthHeight = depthHeight / 2;

        for (int y = 0; y < depthHeight; y += gridStep)
        {
            for (int x = 0; x < depthWidth; x += gridStep)
            {
                int depthIndex = x + (y * depthWidth);
                CameraSpacePoint csp = cameraSpacePoints[depthIndex];

                Vector3 v = Vector3.zero;
                v.x = float.IsInfinity(csp.X) ? (x-halfDepthWidth)/ 500.0f : csp.X;
                v.y = float.IsInfinity(csp.Y) ? (y - halfDepthHeight) / -500.0f : csp.Y;
                v.z = float.IsInfinity(csp.Z) ? DepthMaximum/1000.0f : csp.Z;

                vertices.Add(v);
            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();

        mesh.RecalculateNormals();

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
