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
    public class Cloud
    {
        private NetServer server;
        private double messagesPerSecond = 20.0;
        private Player p1;
        private InputState inState;
        private CloudGameController cloudGC;
        private bool disconnectEvent = false;
        private string disconnectPlayer = "", val = "";
        private long disconnectRID;
        private bool running = false;

        public Cloud(string configName, int port)
        {
            NetPeerConfiguration config = new NetPeerConfiguration(configName);
            config.EnableMessageType(NetIncomingMessageType.DiscoveryRequest);
            config.Port = port;
            config.NetworkThreadName = "Cloud server thread";
            this.server = new NetServer(config);


            cloudGC = new CloudGameController();

            Thread thread = new Thread(new ThreadStart(cloudGC.run));
            thread.Name = "Game Controller thread";
            thread.Start();
        }

        public void Stop()
        {
            this.cloudGC.Stop();
            this.running = false;
        }

        public void Start()
        {
            this.server.Start();
            this.running = true;

            double nextSendUpdates = NetTime.Now;
            double now;

            while (running)
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
                                // Console.WriteLine(NetUtility.ToHexString(msg.SenderConnection.RemoteUniqueIdentifier) + " connected!");
                            }
                            else if (status == NetConnectionStatus.Disconnected)
                            {
                                // closing client makes it timeout and after a few seconds sends this message?
                                Console.WriteLine(msg.SenderConnection.Tag.ToString() + " disconnected!");

                                disconnectEvent = true;
                                if (this.cloudGC.pList.TryGetValue(msg.SenderConnection.RemoteUniqueIdentifier, out val))
                                {
                                    disconnectPlayer = val;
                                    disconnectRID = msg.SenderConnection.RemoteUniqueIdentifier;
                                }
                            }
                            else if (status == NetConnectionStatus.Disconnecting)
                            {
                                // can't get this one to trigger just yet.
                                Console.WriteLine(NetUtility.ToHexString(msg.SenderConnection.RemoteUniqueIdentifier) + " disconnecting!");
                            }
                            break;

                        case NetIncomingMessageType.Data:  // handle message from client

                            //Console.WriteLine(string.Format("got msg from: " + NetUtility.ToHexString(msg.SenderConnection.RemoteUniqueIdentifier)));
                            handleMessage(msg);
                            break;
                    }
                }

                now = NetTime.Now;
                if (now > nextSendUpdates)
                {
                    foreach (NetConnection connection in server.Connections)
                    {
                        // storing client id in Tag.  If it's null, ask client to send it over.
                        // Also send client it's session id.
                        if (connection.Tag == null)
                        {
                            NetOutgoingMessage sendMsg = server.CreateMessage();
                            sendMsg.Write((byte)99);
                            server.SendMessage(sendMsg, connection, NetDeliveryMethod.ReliableUnordered);
                        }
                        else
                        {
                            // tell player of everyone's position including itself.
                            foreach (NetConnection player in server.Connections)
                            {
                                if (player.Tag != null) // have client id
                                {
                                    p1 = this.cloudGC.getPlayer(player.Tag.ToString());
                                    if (p1 != null) // player still connected
                                    {
                                        NetOutgoingMessage sendMsg = server.CreateMessage();
                                        sendMsg.Write((byte)5); // packet id
                                        sendMsg.Write(player.Tag.ToString()); // client id
                                        sendMsg.Write(p1.Position.X);
                                        sendMsg.Write(p1.Position.Y);
                                        sendMsg.Write(p1.Rotation);
                                        server.SendMessage(sendMsg, connection, NetDeliveryMethod.Unreliable);
                                    }
                                }
                            }

                            // tell player of the bullets
                            foreach (Projectile proj in this.cloudGC.getProjectileListCopy())
                            {
                                if (proj.Active)
                                {
                                    NetOutgoingMessage sendMsg = server.CreateMessage();
                                    sendMsg.Write((byte)10); // packet id
                                    sendMsg.Write((byte)proj.ID);
                                    sendMsg.Write(proj.Position.X);
                                    sendMsg.Write(proj.Position.Y);
                                    sendMsg.Write(proj.Rotation);
                                    sendMsg.Write(proj.Shooter);
                                    server.SendMessage(sendMsg, connection, NetDeliveryMethod.Unreliable);
                                }
                            }

                            // tell player of the enemies
                            foreach (Enemy enemy in this.cloudGC.getEnemyListCopy())
                            {
                                NetOutgoingMessage sendMsg = server.CreateMessage();
                                sendMsg.Write((byte)11);
                                sendMsg.Write((byte)enemy.ID);
                                sendMsg.Write(enemy.Position.X);
                                sendMsg.Write(enemy.Position.Y);
                                sendMsg.Write(enemy.Rotation);
                                server.SendMessage(sendMsg, connection, NetDeliveryMethod.Unreliable);
                            }

                            // tell player of collisions
                            foreach (Collision c in this.cloudGC.getCollisionListCopy())
                            {
                                NetOutgoingMessage sendMsg = server.CreateMessage();
                                sendMsg.Write((byte)15); // packet id
                                sendMsg.Write((byte)c.CID); // collision id

                                if (c.CID == 1) // player on player
                                {
                                    sendMsg.Write(c.player1.Name);
                                    sendMsg.Write(c.player2.Name);
                                }
                                else if (c.CID == 2) // player on bullet
                                {
                                    sendMsg.Write(c.player1.Name);
                                    sendMsg.Write((byte)c.projectile.ID);
                                }
                                else if (c.CID == 3) // player on enemy
                                {
                                    sendMsg.Write(c.player1.Name);
                                    sendMsg.Write(c.enemy.ID);
                                }
                                else if (c.CID == 4) // enemy on bullet
                                {
                                    sendMsg.Write((byte)c.enemy.ID);
                                    sendMsg.Write((byte)c.projectile.ID);
                                }
                                server.SendMessage(sendMsg, connection, NetDeliveryMethod.Unreliable);
                            }


                            // inform player someone disconnected
                            if (disconnectEvent)
                            {
                                NetOutgoingMessage sendMsg = server.CreateMessage();
                                sendMsg.Write((byte)1); // packet id
                                sendMsg.Write(disconnectPlayer); // client id
                                server.SendMessage(sendMsg, connection, NetDeliveryMethod.Unreliable);
                            }
                        }
                    }

                    if (disconnectEvent)
                    {
                        disconnectEvent = false;
                        this.cloudGC.removePlayer(disconnectPlayer, disconnectRID);
                    }

                    nextSendUpdates += (1.0 / messagesPerSecond);
                }

                Thread.Sleep(1);
            }

            this.server.Shutdown("Server stopping.");
        }

        protected void handleMessage(NetIncomingMessage msg)
        {
            int packetId = msg.ReadByte();

            switch (packetId)
            {
                case 0: // client ID!
                    if (msg.SenderConnection.Tag == null)
                    {
                        msg.SenderConnection.Tag = msg.ReadString();
                        Console.WriteLine(msg.SenderConnection.Tag.ToString());
                        this.cloudGC.addPlayer(msg.SenderConnection.Tag.ToString(), msg.SenderConnection.RemoteUniqueIdentifier);
                        Console.WriteLine(msg.SenderConnection.Tag.ToString() + " connected!");
                    }
                    break;
                case 1: // keyboards!
                    inState.resetStates();
                    inState.setStates(msg.ReadByte(), msg.ReadByte(), msg.ReadByte(), msg.ReadByte(), msg.ReadByte());
                    this.cloudGC.handleInputState(inState, msg.SenderConnection.Tag.ToString());
                    // tell player new position
                    break;
                default:
                    // unknown packet id
                    break;
            }
        }
    }
}
