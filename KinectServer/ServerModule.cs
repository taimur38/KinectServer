using Nancy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KinectServer
{
    public class ServerModule : NancyModule
    {
        public ServerModule()
        {
            Console.WriteLine(" IN NANCY THING");
            Get["/hi"] = parameters => {
                Console.WriteLine("in method");
                return "Hello, World";
            };
        }
    }
}
