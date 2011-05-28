﻿using System;
using Lidgren.Network;
using Spacestro.Cloud.Library;




// TODO   CREATE A UNIQUE ID FOR THIS CLIENT SO SERVER KNOWS WHO THE HELL THIS IS.



namespace Spacestro
{
    class CloudMessenger
    {
        private NetClient netClient;

        //public event EventHandler<NetIncomingMessageRecievedEventArgs> MessageRecieved;

        public CloudMessenger(string configName)
        {
            NetPeerConfiguration config = new NetPeerConfiguration(configName);
            config.EnableMessageType(NetIncomingMessageType.DiscoveryResponse);

            this.netClient = new NetClient(config);

            this.netClient.Start();

            //TODO need to load this from config or something.
            this.netClient.DiscoverKnownPeer("localhost", 8383);
        }

        /// <summary>
        /// Checks the net client for any recieved messages and fires the MessageRecieved Event for each.
        /// </summary>
        public void CheckForNewMessages()
        {
            NetIncomingMessage msg;
            while ((msg = this.netClient.ReadMessage()) != null)
            {
                switch (msg.MessageType)
                {
                    case NetIncomingMessageType.DiscoveryResponse:
                        // TODO is there anything that needs to be done differently here?
                        // after this, we need to tell server our client id to sync us with existing server data if possible

                        this.netClient.Connect(msg.SenderEndpoint);

                        break;
                    case NetIncomingMessageType.Data:

                        // read message for new position

                        //if (this.MessageRecieved != null)
                        //{
                        //    this.MessageRecieved(this, new NetIncomingMessageRecievedEventArgs(msg));
                        //}
                        break;
                }
            }
        }

        // PacketID 0
        // sends client ID to the server
        public void SendMessage(String id)
        {
            NetOutgoingMessage msg = this.netClient.CreateMessage();
            msg.Write((byte)0);
            msg.Write(id);
            this.netClient.SendMessage(msg, NetDeliveryMethod.Unreliable);
        }

        // PacketID 1
        // sends keyboard message 
        public void SendMessage(Cloud.Library.InputState state)
        {
            NetOutgoingMessage msg = this.netClient.CreateMessage();
            msg.Write((byte)1);
            foreach (bool key in state.getStateList())
            {
                if (key)
                {
                    msg.Write((byte)1);
                }
                else
                {
                    msg.Write((byte)0);
                }
            }
            this.netClient.SendMessage(msg, NetDeliveryMethod.Unreliable);
        }

        
    }
}
