﻿using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lidgren.Network;
using Spacestro.Cloud.Library;
using Spacestro.Entities;

namespace Spacestro.Cloud
{
    class Cloud
    {
        private NetServer server;
        private double messagesPerSecond = 30.0;
        private Player p1;
        private InputState inState;
        private CloudGameController cloudGC;
        private Thread thread;

        //public event EventHandler<NetIncomingMessageRecievedEventArgs> MessageRecieved;

        public Cloud(string configName, int port)
        {
            NetPeerConfiguration config = new NetPeerConfiguration(configName);
            config.EnableMessageType(NetIncomingMessageType.DiscoveryRequest);
            config.Port = port;

            this.server = new NetServer(config);

            cloudGC = new CloudGameController();
            thread = new Thread(new ThreadStart(cloudGC.run));
        }

        public void Start()
        {
            this.server.Start();

            double nextSendUpdates = NetTime.Now;
            double now;

            while (!Console.KeyAvailable || Console.ReadKey().Key != ConsoleKey.Escape)
            {
                NetIncomingMessage msg;
                while ((msg = server.ReadMessage()) != null)
                {
                    switch (msg.MessageType)
                    {
                        case NetIncomingMessageType.DiscoveryRequest:
                            // Respond to the discovery request.
                            server.SendDiscoveryResponse(null, msg.SenderEndpoint);
                            break;



                        case NetIncomingMessageType.VerboseDebugMessage:
                        case NetIncomingMessageType.DebugMessage:
                        case NetIncomingMessageType.WarningMessage:
                        case NetIncomingMessageType.ErrorMessage:
                            // Print the message.
                            // TODO log errors?
                            Console.WriteLine(msg.ReadString());
                            break;



                        case NetIncomingMessageType.StatusChanged:
                            NetConnectionStatus status = (NetConnectionStatus)msg.ReadByte();
                            if (status == NetConnectionStatus.Connected)
                            {
                                Console.WriteLine(NetUtility.ToHexString(msg.SenderConnection.RemoteUniqueIdentifier) + " connected!");
                            }
                            else if (status == NetConnectionStatus.Disconnected)
                            {
                                // closing client makes it timeout and after a few seconds sends this message?
                                Console.WriteLine(NetUtility.ToHexString(msg.SenderConnection.RemoteUniqueIdentifier) + " disconnected!");
                            }
                            else if (status == NetConnectionStatus.Disconnecting)
                            {
                                // can't get this one to trigger just yet.
                                Console.WriteLine(NetUtility.ToHexString(msg.SenderConnection.RemoteUniqueIdentifier) + " disconnecting!");
                            }
                            break;



                        case NetIncomingMessageType.Data:
                            //Console.WriteLine(string.Format("got msg from: " + NetUtility.ToHexString(msg.SenderConnection.RemoteUniqueIdentifier)));

                            handleMessage(msg);
                            //if (this.MessageRecieved != null)
                            //{
                            //    this.MessageRecieved(this, new NetIncomingMessageRecievedEventArgs(msg));
                            //}
                            break;
                    }
                }

                now = NetTime.Now;
                if (now > nextSendUpdates)
                {
                    cloudGC.moveAll();

                    foreach (NetConnection connection in server.Connections)
                    {
                        // storing client id in Tag.  If it's null, ask client to send it over.
                        if (connection.Tag == null)
                        {
                            NetOutgoingMessage sendMsg = server.CreateMessage();
                            sendMsg.Write((byte)99);
                            server.SendMessage(sendMsg, connection, NetDeliveryMethod.ReliableUnordered);
                        }
                        else
                        {
                            // send player position/rotation
                            p1 = cloudGC.getPlayer(connection.Tag.ToString());
                            if (p1 != null)
                            {
                                NetOutgoingMessage sendMsg = server.CreateMessage();
                                sendMsg.Write((byte)5);
                                sendMsg.Write(p1.Position.X);
                                sendMsg.Write(p1.Position.Y);
                                sendMsg.Write(p1.Rotation);
                                server.SendMessage(sendMsg, connection, NetDeliveryMethod.Unreliable);
                            }
                        }


                        // TODO Handle broadcasting messages to connected clients.

                    }
                    nextSendUpdates += (1.0 / messagesPerSecond);
                }

                Thread.Sleep(1);
            }

            Console.WriteLine("Server stopping.");
        }

        protected void handleMessage(NetIncomingMessage msg)
        {
            int packetId = msg.ReadByte();
            // Console.WriteLine(packetId);

            switch (packetId)
            {
                case 0: // client ID!
                    if (msg.SenderConnection.Tag == null)
                    {
                        msg.SenderConnection.Tag = msg.ReadString();
                        Console.WriteLine(msg.SenderConnection.Tag.ToString());
                        cloudGC.addPlayer(msg.SenderConnection.Tag.ToString());
                    }
                    break;
                case 1: // keyboards!
                    inState.resetStates();
                    inState.setStates(msg.ReadByte(), msg.ReadByte(), msg.ReadByte(), msg.ReadByte());
                    cloudGC.handleInputState(inState, msg.SenderConnection.Tag.ToString());
                    // tell player new position
                    break;
                default:
                    // unknown packet id
                    break;
            }
        }
    }
}
