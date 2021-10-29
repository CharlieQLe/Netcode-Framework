using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;
using UnityEngine;

namespace NetcodeFramework.Client {
    public enum ConnectionState {
        Disconnected,
        Connecting,
        Connected
    }

    public delegate void WriteMessageCallback(ref DataStreamWriter stream);
    public delegate void ReceiveMessageCallback(ref DataStreamReader stream);

    public static class ClientManager {
        private static NetworkDriver driver;
        private static NetworkConnection connection;
        private static Dictionary<byte, ReceiveMessageCallback> messageCallbacks;
        private static Queue<Action> mainThreadEventQueue;
        private static JobHandle updateJob;
        private static NetworkPipeline sequencedPipeline;
        private static NetworkPipeline reliablePipeline;
        private static bool hasConnected;
        public static ConnectionState ConnectionState {
            get {
                if (connection.IsCreated) {
                    return hasConnected ? ConnectionState.Connected : ConnectionState.Connecting;
                }
                return ConnectionState.Disconnected;
            }
        }

        static ClientManager() {
            messageCallbacks = new Dictionary<byte, ReceiveMessageCallback>();
            mainThreadEventQueue = new Queue<Action>();
        }

        /// <summary>
        /// Register a message callback.
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="receiveMessageCallback"></param>
        public static void RegisterMessage(byte messageId, ReceiveMessageCallback receiveMessageCallback) => messageCallbacks[messageId] = receiveMessageCallback;

        /// <summary>
        /// Unregister a message callback.
        /// </summary>
        /// <param name="messageId"></param>
        public static void UnregisterMessage(byte messageId) => messageCallbacks.Remove(messageId);

        /// <summary>
        /// Connect to the server at the specified ip and port if possible.
        /// </summary>
        /// <param name="port"></param>
        public static void Connect(string ip, ushort port) {
            if (ConnectionState != ConnectionState.Disconnected || !NetworkEndPoint.TryParse(ip, port, out NetworkEndPoint endPoint)) {
                return;
            }

            // Create the network driver
            driver = NetworkDriver.Create();
            sequencedPipeline = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            reliablePipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            mainThreadEventQueue.Clear();

            // Connect to the endpoint
            connection = driver.Connect(endPoint);
            hasConnected = false;

            Debug.Log("[Client] Attempting to connected to the server...");
        }

        /// <summary>
        /// Disconnect from the server if possible.
        /// </summary>
        public static void Disconnect() {
            if (ConnectionState == ConnectionState.Disconnected) {
                return;
            }

            // Complete the job
            updateJob.Complete();

            // Disconnect from the server
            driver.Disconnect(connection);

            // Immediately send the disconnect message
            driver.ScheduleFlushSend(default).Complete();

            // Dispose the server
            driver.Dispose();

            // Cleanup
            mainThreadEventQueue.Clear();
            connection = default;
            hasConnected = false;

            Debug.Log("[Client] Successfully disconnected from the server!");
        }

        /// <summary>
        /// Send a message to the server.
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="writeMessageCallback"></param>
        /// <param name="sendMode"></param>
        public static void SendMessage(byte messageId, WriteMessageCallback writeMessageCallback, SendMode sendMode = SendMode.Default) {
            mainThreadEventQueue.Enqueue(() => {
                if (ConnectionState == ConnectionState.Connected) {
                    SendMessageInternal(messageId, writeMessageCallback, sendMode);
                }
            });
        }

        private static void SendMessageInternal(byte messageId, WriteMessageCallback writeMessageCallback, SendMode sendMode) {
            if (driver.BeginSend(GetPipeline(sendMode), connection, out DataStreamWriter writer) == 0) {
                writer.WriteByte(messageId);
                writeMessageCallback(ref writer);
                driver.EndSend(writer);
            }
        }

        private static NetworkPipeline GetPipeline(SendMode sendMode) {
            switch (sendMode) {
                case SendMode.Sequenced: return sequencedPipeline;
                case SendMode.Reliable: return reliablePipeline;
                default: return NetworkPipeline.Null;
            }
        }

        private static void BeforeUpdate() {
            updateJob.Complete();
            if (ConnectionState == ConnectionState.Disconnected) {
                return;
            }

            // Process all of the events in the driver's buffer
            NetworkEvent.Type networkEvent;
            while ((networkEvent = driver.PopEvent(out NetworkConnection connection, out DataStreamReader stream)) != NetworkEvent.Type.Empty) {
                switch (networkEvent) {

                    // Process the data from the connection
                    case NetworkEvent.Type.Connect: {
                            Debug.Log("[Client] Successfully connected to the server!");
                            hasConnected = true;
                            break;
                        }

                    // Process the data from the incoming stream
                    case NetworkEvent.Type.Data: {
                            if (stream.Length > 0 && messageCallbacks.TryGetValue(stream.ReadByte(), out ReceiveMessageCallback callback)) {
                                callback(ref stream);
                            }
                            break;
                        }

                    // Process a connection's disconnection from the server
                    case NetworkEvent.Type.Disconnect: {
                            connection = default;
                            hasConnected = false;
                            Debug.Log($"[Client] Server has forced a disconnection! Reason: { (DisconnectReason)stream.ReadByte() }");
                            break;
                        }
                }
            }
        }

        private static void AfterUpdate() {
            if (ConnectionState == ConnectionState.Disconnected) {
                return;
            }

            // Process everything in the main thread queue
            while (mainThreadEventQueue.Count > 0) {
                mainThreadEventQueue.Dequeue()();
            }

            // Schedule job
            updateJob = driver.ScheduleUpdate();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize() {
            Application.quitting += () => {
                Disconnect();
                NetcodeUtility.ResetSubsystems();
            };
            NetcodeUtility.InjectSubsystems(typeof(ClientManager), BeforeUpdate, AfterUpdate);
        }

    }
}
