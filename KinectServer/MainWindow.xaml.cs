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
    public partial class MainWindow : Window
    {
        public KinectSensor sensor = null;
        IList<Body> bodies = null;

        public WebSocketServer server = null;

        public MainWindow()
        {
            InitializeComponent();

            // start server
            Task.Run(() =>
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

            // connect to ableton
            /*using(var ws = new WebSocket("ws://localhost:8181"))
            {
                ws.OnMessage += (sender, e) => Console.WriteLine(e.Data);
                ws.Connect();
                ws.OnClose += (sender, e) => Console.WriteLine("websocket closed");
            }
            */

            sensor = KinectSensor.GetDefault();
            this.bodies = new Body[sensor.BodyFrameSource.BodyCount];
            var bodyReader = sensor.BodyFrameSource.OpenReader();
            bodyReader.FrameArrived += BodyReader_FrameArrived;

            sensor.Open();

            // start websocket server
            this.server = new WebSocketServer("ws://localhost:8181");
            server.AddWebSocketService<ServerThing>("/kinect", () => new ServerThing(sensor));

            Task.Run(() =>
            {
                server.Start();
                Thread.Sleep(Timeout.Infinite);
            });

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

        public string getJointName(JointType j)
        {

            return "asdf";
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
