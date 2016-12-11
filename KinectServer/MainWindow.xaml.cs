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
        Stripes
    }

    public partial class MainWindow : Window
    {
        public KinectSensor sensor = null;
        IList<Body> bodies = null;

        public WebSocketServer server = null;

        ushort[] depthData = null;
        byte[] depthPixels = null;
        FrameDescription depthDescription = null;

        byte[] biFrameData;
        FrameDescription biFrameDescription = null;

        WriteableBitmap bitmap = null;

        int offset = 0;
        int zones = 4;
        int bandWidth = 50;
        bool BassModifier = true;

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
            /*using(var ws = new WebSocket("ws://localhost:8181"))
            {
                ws.OnMessage += (sender, e) => {
                    Console.WriteLine(e.Data);
                    if(bassModifier && e.Data.Bass) {
                        offset++;
                    }
                    else if(bassModifier && !e.Data.Bass) {
                        bassModifier = false;
                    }
                }
                ws.Connect();
                ws.OnClose += (sender, e) => Console.WriteLine("websocket closed");
            }
            */

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

            var rand = new Random();

            Task.Run(() =>
            {

                while(true)
                {
                    if(this.BassModifier)
                        offset++;
                    //bandWidth = rand.Next(30, 70);
                    Thread.Sleep(100);
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

                int colorPixelIndex = 0;
                int mapDepthToByte = maxDepth / 256;

                for (int i = 0; i < this.depthData.Length; i++)
                {
                    ushort depth = depthData[i];

                    int intensity = depth >= 0 && depth <= maxDepth ? depth / mapDepthToByte : 0;


                    depth = depth >= 0 && depth <= maxDepth ? depth : (ushort)0;
                    int zone = depth / bandWidth;

                    if(biFrameData[i] < (byte)bodies.Count)
                    {
                        if(!BassModifier)
                        {
                            switch ((zone % zones + offset) % zones)
                            {
                                case 0:
                                    this.depthPixels[colorPixelIndex++] = (byte)(10);
                                    this.depthPixels[colorPixelIndex++] = (byte)(200);
                                    this.depthPixels[colorPixelIndex++] = (byte)(40);
                                    break;
                                case 1:
                                    this.depthPixels[colorPixelIndex++] = (byte)(200);
                                    this.depthPixels[colorPixelIndex++] = (byte)(200);
                                    this.depthPixels[colorPixelIndex++] = (byte)(40);
                                    break;
                                case 2:
                                    this.depthPixels[colorPixelIndex++] = (byte)0;
                                    this.depthPixels[colorPixelIndex++] = (byte)10;
                                    this.depthPixels[colorPixelIndex++] = (byte)240;
                                    break;
                                case 3:
                                    this.depthPixels[colorPixelIndex++] = (byte)10;
                                    this.depthPixels[colorPixelIndex++] = (byte)200;
                                    this.depthPixels[colorPixelIndex++] = (byte)200;
                                    break;
                                case 4:
                                    this.depthPixels[colorPixelIndex++] = (byte)0;
                                    this.depthPixels[colorPixelIndex++] = (byte)0;
                                    this.depthPixels[colorPixelIndex++] = (byte)0;
                                    break;
                            }
                        }
                        else
                        {
                            this.depthPixels[colorPixelIndex++] = (byte)0;
                            this.depthPixels[colorPixelIndex++] = (byte)0;
                            this.depthPixels[colorPixelIndex++] = (byte)0;
                        }
                    }
                    else
                    {
                        if(!BassModifier)
                        {
                            this.depthPixels[colorPixelIndex++] = (byte)intensity;
                            this.depthPixels[colorPixelIndex++] = (byte)intensity;
                            this.depthPixels[colorPixelIndex++] = (byte)intensity;
                        }
                        else
                        {
                            switch ((zone % zones + offset) % zones)
                            {
                                case 0:
                                    this.depthPixels[colorPixelIndex++] = (byte)(10);
                                    this.depthPixels[colorPixelIndex++] = (byte)(200);
                                    this.depthPixels[colorPixelIndex++] = (byte)(40);
                                    break;
                                case 1:
                                    this.depthPixels[colorPixelIndex++] = (byte)(200);
                                    this.depthPixels[colorPixelIndex++] = (byte)(200);
                                    this.depthPixels[colorPixelIndex++] = (byte)(40);
                                    break;
                                case 2:
                                    this.depthPixels[colorPixelIndex++] = (byte)0;
                                    this.depthPixels[colorPixelIndex++] = (byte)10;
                                    this.depthPixels[colorPixelIndex++] = (byte)240;
                                    break;
                                case 3:
                                    this.depthPixels[colorPixelIndex++] = (byte)10;
                                    this.depthPixels[colorPixelIndex++] = (byte)200;
                                    this.depthPixels[colorPixelIndex++] = (byte)200;
                                    break;
                                case 4:
                                    this.depthPixels[colorPixelIndex++] = (byte)0;
                                    this.depthPixels[colorPixelIndex++] = (byte)0;
                                    this.depthPixels[colorPixelIndex++] = (byte)0;
                                    break;
                            }
                        }
                    }
                    //this.depthPixels[colorPixelIndex++] = 255;
                    colorPixelIndex++;

                }

                this.bitmap.Lock();
                this.DisplayImage.Source = BitmapSource.Create(dfDescrip.Width, dfDescrip.Height, 96, 96, PixelFormats.Bgr32, null, depthPixels, dfDescrip.Width * PixelFormats.Bgr32.BitsPerPixel / 8);
                //this.bitmap.WritePixels(new Int32Rect(0, 0, this.bitmap.PixelWidth, this.bitmap.PixelHeight), depthPixels, dfDescrip.Width * PixelFormats.Bgr32.BitsPerPixel / 8, 0);
                //depthPixels.CopyTo(this.bitmap.PixelBuffer);
                this.bitmap.Unlock();
                //this.DisplayImage.Source = this.bitmap;

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

        private void Bass_Click(object sender, RoutedEventArgs e)
        {
            this.BassModifier = !this.BassModifier;
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
}
