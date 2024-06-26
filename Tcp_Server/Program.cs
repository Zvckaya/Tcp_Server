﻿using System.Net;
using System.Net.Sockets;
using System.Text;
using Tcp_Server_Core;

namespace Tcp_Server
{


    class Program
    {
        static Listener _listener = new Listener();
        public static GameRoom Room = new GameRoom();



        static void Main(string[] args)
        {
            string host = Dns.GetHostName();
            IPHostEntry ipHost = Dns.GetHostEntry(host);
            IPAddress ipAddr = ipHost.AddressList[0];
            IPEndPoint endPoint = new IPEndPoint(ipAddr, 7777);

            _listener.Init(endPoint, () => { return SessionManager.instance.Generate(); });
            Console.WriteLine("Listening...");
 

            while (true)
            {
                Room.Push(() => Room.Flush());
                Thread.Sleep(250);
                
            }


        }
    }
}