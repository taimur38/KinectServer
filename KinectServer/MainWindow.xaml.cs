using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Nancy.Hosting.Self;
using System.Threading;
using Microsoft.Kinect;
using WebSocketSharp;
using WebSocketSharp.Server;
using Newtonsoft.Json;
using System.Dynamic;

namespace KinectServer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    public enum ViewMode
    {
       Zones,
       BodyMask,
       Outline
    }

    public partial class MainWindow : Window
    {

        public ViewMode CurrentViewMode = ViewMode.Zones;

        public KinectSensor sensor = null;
        IList<Body> bodies = null;

        public WebSocketServer server = null;

        ushort[] depthData = null;
        byte[] depthPixels = null;
        FrameDescription depthDescription = null;

        byte[] biFrameData;
        FrameDescription biFrameDescription = null;

        byte[] colorFrameData;
        DepthSpacePoint[] colorDepthPoints;

        WriteableBitmap bitmap = null;

        int offset = 0;
        int offsetMultiplier = 1;

        Tuple<byte, byte, byte> personColor = new Tuple<byte, byte, byte>(10, 200, 200);

        Tile t = new Tile();

        int zones = 80;
        int bandWidth = 15;

        bool BassModifier = true;

        static Dictionary<int, RGBA> depthColorMap;

        public MainWindow()
        {
            InitializeComponent();

            // start server
            /*Task.Run(() =>
            {
                var hostConfiguration = new HostConfiguration()
                {
                    UrlReservations = new UrlReservations()
                    {
                        CreateAutomatically = true,
                        User = "taimu"
                    }
                };
                using (var host = new NancyHost(hostConfiguration, new Uri("http://localhost:8080")))
                {
                    host.Start();
                    Console.WriteLine("running on http://localhost:8080");
                    Console.ReadLine();
                    Console.WriteLine("shit");
                    Thread.Sleep(Timeout.Infinite);
                }
            });
            */

            // connect to ableton
            Task.Run(() =>
            {
                var ws = new WebSocket("ws://192.168.1.130:1337");
                ws.Connect();
                ws.OnMessage += (sender, e) =>
                {
                    var note = int.Parse(e.Data);
                    Console.WriteLine(e.Data);
                    if (note == 60)
                    {
                        BassModifier = !BassModifier;
                        offset++;
                    }
                    else if (note != 60)
                    {
                        BassModifier = false;
                    }
                };
                ws.Connect();
                ws.OnClose += (sender, e) => Console.WriteLine("websocket closed");
            });

            depthColorMap = new Dictionary<int, RGBA>();
            sensor = KinectSensor.GetDefault();
            sensor.Open();
            this.bodies = new Body[sensor.BodyFrameSource.BodyCount];
            var bodyReader = sensor.BodyFrameSource.OpenReader();
            bodyReader.FrameArrived += BodyReader_FrameArrived;

            var depthReader = sensor.DepthFrameSource.OpenReader();
            depthReader.FrameArrived += DepthReader_FrameArrived;

            depthDescription = sensor.DepthFrameSource.FrameDescription;
            bitmap = new WriteableBitmap(depthDescription.Width, depthDescription.Height, 96.0, 96.0, PixelFormats.Bgr32, null);
            depthData = new ushort[depthDescription.Width * depthDescription.Height];
            depthPixels = new byte[depthDescription.Width * depthDescription.Height  * (PixelFormats.Bgr32.BitsPerPixel + 7)/8];

            var biReader = sensor.BodyIndexFrameSource.OpenReader();
            biFrameDescription = sensor.BodyIndexFrameSource.FrameDescription;
            biReader.FrameArrived += BiReader_FrameArrived;
            biFrameData = new byte[biFrameDescription.Width * biFrameDescription.Height];

            var colorReader = sensor.ColorFrameSource.OpenReader();
            colorReader.FrameArrived += ColorReader_FrameArrived;
            var colorDescrip = sensor.ColorFrameSource.FrameDescription;
            colorFrameData = new byte[colorDescrip.LengthInPixels * 4];
            colorDepthPoints = new DepthSpacePoint[colorDescrip.LengthInPixels];


            var rand = new Random();

            Task.Run(() =>
            {

                while(true)
                {
                    if(this.BassModifier)
                    {
                        offset = offset + offsetMultiplier;
                        //bandWidth = Math.Max((bandWidth + 3) % 30, 15);
                    }
                    //bandWidth = rand.Next(30, 70);
                    Thread.Sleep(100);
                }
            });

            Task.Run(() => {
                while(true)
                {
                    bandWidth = bandWidth == 15 ? 50 : 15;
                    Thread.Sleep(5000);
                }
            });

            // start websocket server
            this.server = new WebSocketServer("ws://localhost:8181");
            server.AddWebSocketService<ServerThing>("/kinect", () => new ServerThing(sensor));

            Task.Run(() =>
            {
                server.Start();
                Thread.Sleep(Timeout.Infinite);
            });

        }

        #region FrameArrivals

        private void ColorReader_FrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            using(var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null)
                    return;

                this.sensor.CoordinateMapper.MapColorFrameToDepthSpace(depthData, colorDepthPoints);
                frame.CopyConvertedFrameDataToArray(this.colorFrameData, ColorImageFormat.Bgra);

                if(CurrentViewMode == ViewMode.BodyMask)
                    DrawGlitchyBodyMask();
            }
        }

        private void BiReader_FrameArrived(object sender, BodyIndexFrameArrivedEventArgs e)
        {
            using(var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null)
                    return;

                var frameDescription = frame.FrameDescription;
                frame.CopyFrameDataToArray(biFrameData);
            }
        }

        private void DepthReader_FrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            using (var depthFrame = e.FrameReference.AcquireFrame())
            {
                if (depthFrame == null)
                    return;

                var dfDescrip = depthFrame.FrameDescription;

                ushort minDepth = 0;
                ushort maxDepth = 0;

                if (((dfDescrip.Width * dfDescrip.Height) != this.depthData.Length) || (dfDescrip.Width != this.bitmap.PixelWidth) || (dfDescrip.Height != this.bitmap.PixelHeight))
                {
                    Console.WriteLine("descripArea: {0}, depthDataLength: {1}", dfDescrip.Width * dfDescrip.Height, this.depthData.Length);
                    Console.WriteLine("descripWidth: {0}, bitmapWidth: {1}", dfDescrip.Width, bitmap.PixelWidth);
                    Console.WriteLine("descripHeight: {0}, bitmapHeight: {1}", dfDescrip.Height, bitmap.PixelHeight);
                    return;
                }

                depthFrame.CopyFrameDataToArray(this.depthData);

                minDepth = depthFrame.DepthMinReliableDistance;
                maxDepth = depthFrame.DepthMaxReliableDistance;

                if(CurrentViewMode == ViewMode.Zones)
                    DrawDepthZones(dfDescrip, minDepth, maxDepth);
                if (CurrentViewMode == ViewMode.Outline)
                    DrawDepthOutline(dfDescrip);
            }
        }

        private void BodyReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            using(var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null)
                    return;

                frame.GetAndRefreshBodyData(this.bodies);

                bool atleastOne = false;

                dynamic obj = new ExpandoObject();

                var bodyArray = new List<dynamic>();
                for(int i = 0; i < this.bodies.Count; i++)
                {
                    var body = this.bodies[i];
                    if (body == null || !body.IsTracked)
                        continue;

                    atleastOne = true;
                    dynamic jsonBody = new ExpandoObject();
                    jsonBody.TrackingId = body.TrackingId;
                    var jointList = new dynamic[body.Joints.Keys.Count()];
                    int j = 0;
                    foreach (var jointType in body.Joints.Keys)
                    {
                        dynamic jsonJoint = new ExpandoObject();
                        jsonJoint.type = jointType.ToString();
                        jsonJoint.x = body.Joints[jointType].Position.X;
                        jsonJoint.y = body.Joints[jointType].Position.Y;
                        jsonJoint.z = body.Joints[jointType].Position.Z;
                        jsonJoint.trackingState = body.Joints[jointType].TrackingState.ToString();

                        jointList[j++] = jsonJoint;
                    }
                    jsonBody.joints = jointList;
                    bodyArray.Add(jsonBody);
                }

                obj.bodies = bodyArray;

                if(atleastOne)
                    server.WebSocketServices.Broadcast(JsonConvert.SerializeObject(obj));
            }
        }
        #endregion

        #region Draws

        struct RGBA
        {
            public byte r;
            public byte g;
            public byte b;
            public byte a;
        }

        unsafe void DrawGlitchyBodyMask()
        {
            var bandWidth = 100;
            for (int i = 0; i < colorDepthPoints.Length; i++)
            {
                var r = colorFrameData[i * 4 + 0];
                var g = colorFrameData[i * 4 + 1];
                var b = colorFrameData[i * 4 + 2];
                var alpha = colorFrameData[i * 4 + 3];

                var colorDepthPoint = colorDepthPoints[i];
                if (colorDepthPoint.X >= 0 && colorDepthPoint.X < depthDescription.Width && colorDepthPoint.Y >= 0 && colorDepthPoint.Y < depthDescription.Height)
                {
                    var depthIndex = (int)(colorDepthPoint.X + 0.5f) + (int)(colorDepthPoint.Y + 0.5f) * depthDescription.Width;
                    var biPoint = this.biFrameData[depthIndex];
                    var depth = this.depthData[depthIndex];

                    var converted = Convert.ToInt32(biPoint) * 10000;

                    if(biPoint < (byte)this.bodies.Count)
                    {

                        var key = converted + depth / bandWidth;
                        if(!depthColorMap.ContainsKey(key))
                        {
                            var rgba = new RGBA();
                            rgba.r = r;
                            rgba.g = g;
                            rgba.b = b;
                            rgba.a = alpha;

                            depthColorMap.Add(key, rgba);
                            this.depthPixels[depthIndex * 4 + 0] = r;
                            this.depthPixels[depthIndex * 4 + 1] = g;
                            this.depthPixels[depthIndex * 4 + 2] = b;
                            this.depthPixels[depthIndex * 4 + 3] = alpha;
                        }
                        else
                        {
                            var color = depthColorMap[key];
                            /*color.r = (byte)((color.r + r) / 2);
                            color.g = (byte)((color.g + r) / 2);
                            color.b = (byte)((color.b + r) / 2);
                            color.a = (byte)((color.a + r) / 2);
                            */

                            this.depthPixels[depthIndex * 4 + 0] = color.r;
                            this.depthPixels[depthIndex * 4 + 1] = color.g;
                            this.depthPixels[depthIndex * 4 + 2] = color.b;
                            this.depthPixels[depthIndex * 4 + 3] = color.a;
                        }
                    }
                    else
                    {
                        this.depthPixels[depthIndex * 4 + 0] = r;
                        this.depthPixels[depthIndex * 4 + 1] = g;
                        this.depthPixels[depthIndex * 4 + 2] = b;
                        this.depthPixels[depthIndex * 4 + 3] = alpha;
                    }

                }

            }
            DrawDepthPixels();
        }

        void DrawDepthOutline(FrameDescription dfDescrip)
        {

            var width = dfDescrip.Width;
            var threshold = (byte)bodies.Count;
            var colorPixelIndex = 0;

            // depthData is set
            // pass 1
            for (int i = 0; i < this.depthData.Length; i++)
            {
                ushort depth = depthData[i];

                t.UpdateTile(i, this.biFrameData, width);
                var sum = t.Sum((byte)bodies.Count);

                if(sum > 0 && sum < 8)
                { 
                    var color = getZoneColors(depth);
                    this.depthPixels[colorPixelIndex++] = (byte)color.Item1;
                    this.depthPixels[colorPixelIndex++] = (byte)color.Item2;
                    this.depthPixels[colorPixelIndex++] = (byte)color.Item3;
                }
                else
                {
                    this.depthPixels[colorPixelIndex++] = (byte)0;
                    this.depthPixels[colorPixelIndex++] = (byte)0;
                    this.depthPixels[colorPixelIndex++] = (byte)0;
                }
                this.depthPixels[colorPixelIndex++] = (byte)255;
            }
            DrawDepthPixels();
        }

        void DrawDepthZones(FrameDescription dfDescrip, ushort minDepth, ushort maxDepth)
        {
            int colorPixelIndex = 0;
            int mapDepthToByte = maxDepth / 256;
            // depthData is set
            for (int i = 0; i < this.depthData.Length; i++)
            {
                ushort depth = depthData[i];

                int intensity = depth >= 0 && depth <= maxDepth ? depth / mapDepthToByte : 0;

                //return new Tuple<byte, byte, byte>(10, 200, 200);

                depth = depth >= 0 && depth <= maxDepth ? depth : (ushort)0;

                if(biFrameData[i] < (byte)bodies.Count)
                {
                    if(!BassModifier)
                    {
                        var colors = getZoneColors(depth);
                        this.depthPixels[colorPixelIndex++] = colors.Item1;
                        this.depthPixels[colorPixelIndex++] = colors.Item2;
                        this.depthPixels[colorPixelIndex++] = colors.Item3;
                    }
                    else
                    {
                        this.depthPixels[colorPixelIndex++] = personColor.Item1;
                        this.depthPixels[colorPixelIndex++] = personColor.Item2;
                        this.depthPixels[colorPixelIndex++] = personColor.Item3;
                    }
                }
                else
                {
                    if(!BassModifier)
                    {
                        /*
                        this.depthPixels[colorPixelIndex++] = (byte)intensity;
                        this.depthPixels[colorPixelIndex++] = (byte)intensity;
                        this.depthPixels[colorPixelIndex++] = (byte)intensity;
                        */
                        this.depthPixels[colorPixelIndex++] = 0;
                        this.depthPixels[colorPixelIndex++] = 0;
                        this.depthPixels[colorPixelIndex++] = 0;
                    }
                    else
                    {
                        var colors = getZoneColors(depth);
                        this.depthPixels[colorPixelIndex++] = colors.Item1;
                        this.depthPixels[colorPixelIndex++] = colors.Item2;
                        this.depthPixels[colorPixelIndex++] = colors.Item3;
                    }
                }
                this.depthPixels[colorPixelIndex++] = 255;    // no alpha channel
                //colorPixelIndex++;
            }

            DrawDepthPixels();

        }

        void DrawDepthPixels()
        {
            this.bitmap.Lock();
            //this.DisplayImage.Source = BitmapSource.Create(dfDescrip.Width, dfDescrip.Height, 96, 96, PixelFormats.Bgr32, null, depthPixels, dfDescrip.Width * PixelFormats.Bgr32.BitsPerPixel / 8);
            this.bitmap.WritePixels(new Int32Rect(0, 0, this.bitmap.PixelWidth, this.bitmap.PixelHeight), depthPixels, depthDescription.Width * PixelFormats.Bgra32.BitsPerPixel / 8, 0);
            this.DisplayImage.Source = this.bitmap;
            this.bitmap.Unlock();
        }

        Tuple<byte, byte, byte> getZoneColors(ushort depth)
        {

            int zone = depth / bandWidth;
            switch (((zone % zones + offset) % (zones / 4)))
            {
                case 0:
                    return new Tuple<byte, byte, byte>(75, 75, 0);
                    //return new Tuple<byte, byte, byte>(10, 200, 40);
                case 1:
                    return new Tuple<byte, byte, byte>(0, 75, 200);
                    //return new Tuple<byte, byte, byte>(200, 200, 40);
                case 2:
                    return new Tuple<byte, byte, byte>(120, 75, 0);    // dark green thing?
                    //return new Tuple<byte, byte, byte>(0, 10, 240);
                case 3:
                    return new Tuple<byte, byte, byte>(240, 32, 160);       // purple
                    //return new Tuple<byte, byte, byte>(10, 200, 200);
                case 4:
                    return new Tuple<byte, byte, byte>(0, 0, 0);
                default:
                    return new Tuple<byte, byte, byte>(0, 0, 0);
            }
        }

        #endregion

        private void Bass_Click(object sender, RoutedEventArgs e)
        {
            this.BassModifier = !this.BassModifier;
            zones = zones == 80 ? 4 * 4 : 80;
            bandWidth = 50;
        }
    }

    public class ServerThing : WebSocketBehavior
    {
        public ServerThing(KinectSensor sensor)
        {
            sensor.Open();
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            Sessions.Broadcast("hi");
        }
    }

    unsafe public class Tile
    {
        public Tile()
        {
            this.tileBorder = new byte[8];
            this.invalid = true;
        }

        public bool invalid { get; set; }
        public byte[] tileBorder { get; set; }
        public byte main { get; set; }

        public void UpdateTile(int i, byte[] byteArr, int width)
        {
            if(i - width - 1 < 0 || i + width + 1 >= byteArr.Length)
            {
                this.invalid = true;
                this.main = byteArr[i];
                return;
            }
            this.invalid = false;

            tileBorder[0] = byteArr[i - width - 1];
            tileBorder[1] = byteArr[i - width];
            tileBorder[2] = byteArr[i - width + 1];

            tileBorder[3] = byteArr[i - 1];
            tileBorder[4] = byteArr[i + 1];

            tileBorder[5] = byteArr[i + width - 1];
            tileBorder[6] = byteArr[i + width];
            tileBorder[7] = byteArr[i + width + 1];
        }

        public int Sum(byte threshold)
        {
            if (this.invalid)
                return -1;

            int sum = 0;
            foreach (byte b in this.tileBorder)
                sum += b > threshold ? 1 : 0;

            return sum;
        }
    }
}
