﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Tcp_Server_Core
{
    public abstract class PacketSession: Session
    {
        public static readonly int HeaderSize = 2;

        public sealed override int OnRecv(ArraySegment<byte> buffer)  // sealed를 사용하면 override를 사용할 수 없다.
        {
            int processLen = 0;
            int packetCount = 0;

            while (true)
            {
                //헤더 파싱이 가능한지 판별 
                if (buffer.Count < HeaderSize)
                    break;

                //패킷이 완전체로 도착했는지 확인
                ushort dataSize = BitConverter.ToUInt16(buffer.Array, buffer.Offset); //사이즈를 확인하기 위함
                if (buffer.Count < dataSize)
                    break; //데이터가 완전체로 도착하지 않았다면, 루프를 빠져나감

                //패킷 조립 가능
                OnRecvPacket(new ArraySegment<byte>(buffer.Array,buffer.Offset,dataSize)); //ArraySegment는 구조체임, 즉 스택에 복사를함
                packetCount++;

                processLen += dataSize;
                buffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + dataSize, buffer.Count - dataSize);
            }

            if(packetCount > 1)
                Console.WriteLine($"패킷 모아보내기:{packetCount}");

            return processLen;
        }
        public abstract void OnRecvPacket(ArraySegment<byte> buffer);
    }

    public abstract class Session
    {
        Socket _socket;
        int _disconnected = 0;
        RecvBuffer _recvBuffer = new RecvBuffer(65535);


        Queue<ArraySegment<byte>> _sendQueue = new Queue<ArraySegment<byte>>();
        object _lock = new object();


        List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>(); // List로 만들어서 여러개의 버퍼를 보낼 수 있도록 함
        SocketAsyncEventArgs _sendArgs = new SocketAsyncEventArgs();
        SocketAsyncEventArgs _recvArgs = new SocketAsyncEventArgs();

        public abstract void OnConnected(EndPoint endPoint);
        public abstract int OnRecv(ArraySegment<byte> buffer);
        public abstract void OnSend(int numOfBytes);
        public abstract void OnDisconnected(EndPoint endPoint);

        void Clear()
        {
            lock (_lock)
            {
                _sendQueue.Clear();
                _pendingList.Clear();
            }

        }

        public void Start(Socket socket)
        {
            _socket = socket;
            _recvArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnRecvCompleted);
            _sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendCompleted);

            RegisterRecv();

        }

        public void Send(List<ArraySegment<byte>> sendBuffList)
        {
            if (sendBuffList.Count == 0)
                return;

            lock (_lock)  //동시에 Send를 호출 했을때, 큐에 넣는 작업을 동기화 시킴
            {
                foreach (ArraySegment<byte> sendBuff in sendBuffList)
                {
                    _sendQueue.Enqueue(sendBuff);
                }
                
                if (_pendingList.Count == 0) //보내는중이 아니라면, 보내기 시작
                    RegisterSend();

            }
        }

        public void Send(ArraySegment<byte> sendBuff)
        {
            lock (_lock)  //동시에 Send를 호출 했을때, 큐에 넣는 작업을 동기화 시킴
            {
                _sendQueue.Enqueue(sendBuff);
                if (_pendingList.Count == 0) //보내는중이 아니라면, 보내기 시작
                    RegisterSend();

            }
        }


        public void Disconnect()
        {
            if (Interlocked.Exchange(ref _disconnected, 1) == 1)  //끊긴 상태 확인, 멀티스레드 상황에서의 충돌방지
                return;

            OnDisconnected(_socket.RemoteEndPoint);

            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
            Clear();
        }

        #region 네트워크 통신

        void RegisterSend()
        {
            if (_disconnected == 1)
                return;

            while (_sendQueue.Count > 0)
            {
                ArraySegment<byte> buff = _sendQueue.Dequeue();
                _pendingList.Add(buff); //List에 추가
            }
            _sendArgs.BufferList = _pendingList;

            try
            {
                bool pending = _socket.SendAsync(_sendArgs); //재사용 가능한 현태 
                if (pending == false)
                    OnSendCompleted(null, _sendArgs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.ToString()}");
            }


            
        }
         
        void OnSendCompleted(object sender, SocketAsyncEventArgs args)
        {
            //Callback방식으로 다른 스레드에서 호출 될 수 있기 때문에, lock을 걸어줌
            lock (_lock)
            {
                if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
                {
                    try
                    {
                        _sendArgs.BufferList = null; //보낸 데이터를 비워줌
                        _pendingList.Clear();

                        OnSend(_sendArgs.BytesTransferred);

                        if (_sendQueue.Count > 0) // 보내는도중, 큐에 데이터가 들어오면 다시 Send를 호출
                            RegisterSend();

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"OnSendCompleted Failed {e.Message}");
                    }
                }
                else
                {
                    Disconnect();
                }

            }




        }

        void RegisterRecv()
        {
            _recvBuffer.Clean(); //기존에 읽은 데이터를 지워줌 커서의 초과 이동방지 
            ArraySegment<byte> segment =  _recvBuffer.WriteSegment;
            _recvArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

            try
            {
                bool pending = _socket.ReceiveAsync(_recvArgs);
                if (pending == false)
                    OnRecvCompleted(null, _recvArgs);
            }
            catch (Exception ex) {
                Console.WriteLine($"Register Failed{ex}");
            }

     
        }

        void OnRecvCompleted(object sender, SocketAsyncEventArgs args)
        {
            if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
            {
                try
                {
                    if(_recvBuffer.OnWrite(args.BytesTransferred) == false) // 수신받은 byte만큼 이동했나?
                    {
                        Disconnect();
                        return;
                    }
                    //컨텐츠 쪽으로 데이터를 넘겨줌 데이터 범위만큼 찝어줌
                    
                    int processLen = OnRecv(_recvBuffer.ReadSegment);

                    if(processLen <0 || _recvBuffer.DataSize < processLen)
                    {
                        Disconnect();
                        return;
                    }

                    //Read 커서 이동
                    if(_recvBuffer.OnRead(processLen) == false)
                    {
                        Disconnect();
                        return;
                    }

                    RegisterRecv(); //다시 재등록 
                }
                catch (Exception e)
                {
                    Console.WriteLine($"OnRecvCompleted Failed {e.Message}");
                }

            }
            else
            {

            }
        }

        #endregion

    }
}
