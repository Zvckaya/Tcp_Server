﻿using System.Net;
using System.Net.Sockets;
using System.Text;
using Tcp_Server_Core;

namespace Dummy_Client
{


    class Program
    {
        static void Main(string[] args)
        {
            string host = Dns.GetHostName();
            IPHostEntry ipHost = Dns.GetHostEntry(host);
            IPAddress ipAddr = ipHost.AddressList[0];
            IPEndPoint endPoint = new IPEndPoint(ipAddr, 7777);

            Connector connector = new Connector();

            connector.Connect(endPoint, () => { return SessionManager.Instance.Generate(); },100);

            while (true)
            {
              
                try
                {
                    SessionManager.Instance.SendForEach();
                }
                catch (SocketException e)
                {
                    Console.WriteLine(e.Message);
                }
                Thread.Sleep(250);
            }
        }
    }
}