using UnityEngine;
using System.Collections;
using Windows.Kinect;

public class KinectMapper01 : MonoBehaviour {
    
    KinectSensor sensor;
    MultiSourceFrameReader reader;

    // PUBLIC:
    public ushort DepthMinimum = 500;
    public ushort DepthMaximum = 2500;
    public bool ShowBodiesOnly = true;

    // PRIVATE:
    ushort[] depthData;
    byte[] colorData;
    byte[] bodyIndexData;

    //int depthWidth, depthHeight, 
    int colorWidth;

    uint colorBytesPerPixel, colorLengthInBytes;

    CoordinateMapper coordinateMapper;
    ColorSpacePoint[] colorSpacePoints;
    byte[] mappedColorData;

    Texture2D texture;


    void Start () {

        sensor = KinectSensor.GetDefault();

        if ( sensor != null )
        {
            reader = sensor.OpenMultiSourceFrameReader(
                FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.BodyIndex);

            coordinateMapper = sensor.CoordinateMapper;

            FrameDescription depthFrameDesc = sensor.DepthFrameSource.FrameDescription;
            depthData = new ushort[depthFrameDesc.LengthInPixels];
            //depthWidth = depthFrameDesc.Width;
            //depthHeight = depthFrameDesc.Height;
            
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

            // STEP 2. PREPARE THE BYTE ARRAY THAT WILL HOLD A CALCULATED COLOR PIXEL FOR EACH DEPTH PIXEL
            //         THIS BYTE ARRAY WILL BE FED TO THE MATERIAL AND USED AS THE MESH TEXTURE
            mappedColorData = new byte[depthFrameDesc.LengthInPixels * colorFrameDesc.BytesPerPixel];

            // STEP 3. CREATE A TEXTURE THAT HAS THE SIZE OF THE DEPTH FRAME BUT CAN HOLD RGBA VALUES FROM THE COLOR FRAME
            texture = new Texture2D(depthFrameDesc.Width, depthFrameDesc.Height, TextureFormat.RGBA32, false);

            // STEP 4. BIND THE MAIN TEXTURE TO THE LOCAL VARIABLE FOR FUTURE PROCESSING
            gameObject.GetComponent<Renderer>().material.mainTexture = texture;
            
            if (!sensor.IsOpen) sensor.Open();

        } else
        {
            Debug.LogError("Couldn't find Kinect Sensor!");
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
