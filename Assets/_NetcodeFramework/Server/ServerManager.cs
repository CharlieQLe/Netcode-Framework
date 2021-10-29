using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;
using UnityEngine;

namespace NetcodeFramework.Server {
    public delegate void WriteMessageCallback(ref DataStreamWriter stream);
    public delegate void ReceiveMessageCallback(in NetworkConnection connection, ref DataStreamReader stream);

    public static class ServerManager {
        private static NetworkDriver driver;
        private static HashSet<NetworkConnection> connections;
        private static Dictionary<byte, ReceiveMessageCallback> messageCallbacks;
        private static Queue<Action> mainThreadEventQueue;
        private static JobHandle updateJob;
        private static NetworkPipeline sequencedPipeline;
        private static NetworkPipeline reliablePipeline;
    
        public static bool IsRunning => driver.IsCreated;
        public static int ConnectionCount => connections.Count;

        static ServerManager() {
            connections = new HashSet<NetworkConnection>();
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
        /// Start the server on the specified port if possible.
        /// </summary>
        /// <param name="port"></param>
        public static void Start(ushort port) {
            if (IsRunning) {
                return;
            }

            // Create the network driver
            driver = NetworkDriver.Create();

            // Determine the endpoint
            NetworkEndPoint endpoint = NetworkEndPoint.AnyIpv4;
            endpoint.Port = port;

            // Attempt to bind
            if (driver.Bind(endpoint) != 0) {
                Debug.LogError("[Server] Failed to bind endpoint!");
                driver.Dispose();
            } else if (driver.Listen() != 0) {
                Debug.LogError("[Server] Failed to listen!");
                driver.Dispose();
            } else {
                Debug.Log($"[Server] Successfully started server on port { port }!");
                sequencedPipeline = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
                reliablePipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                mainThreadEventQueue.Clear();
            }
        }

        /// <summary>
        /// Stop the server if possible.
        /// </summary>
        public static void Stop() {
            if (!IsRunning) {
                return;
            }

            // Complete the job
            updateJob.Complete();

            // Disconnect all of the connections
            foreach (NetworkConnection connection in connections) {
                driver.Disconnect(connection);
            }

            // Immediately send the disconnect message
            driver.ScheduleFlushSend(default).Complete();

            // Dispose the server
            driver.Dispose();

            // Cleanup
            mainThreadEventQueue.Clear();

            Debug.Log("[Server] Successfully stopped server!");
        }

        /// <summary>
        /// Disconnect the specified connection
        /// </summary>
        /// <param name="connection"></param>
        public static void Disconnect(NetworkConnection connection) {
            mainThreadEventQueue.Enqueue(() => {
                if (!connection.IsCreated) {
                    return;
                }
                driver.Disconnect(connection);

                // todo: check if disconnect gets called

            });
        }

        /// <summary>
        /// Send a message to a connection.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="messageId"></param>
        /// <param name="writeMessageCallback"></param>
        /// <param name="sendMode"></param>
        public static void SendMessageTo(NetworkConnection connection, byte messageId, WriteMessageCallback writeMessageCallback, SendMode sendMode = SendMode.Default) {
            mainThreadEventQueue.Enqueue(() => {
                if (connection.IsCreated) {
                    SendMessageInternal(connection, messageId, writeMessageCallback, sendMode);
                }
            });
        }

        /// <summary>
        /// Send a message to every connection.
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="writeMessageCallback"></param>
        /// <param name="sendMode"></param>
        public static void SendMessageToAll(byte messageId, WriteMessageCallback writeMessageCallback, SendMode sendMode = SendMode.Default) {
            mainThreadEventQueue.Enqueue(() => {
                foreach (NetworkConnection connection in connections) {
                    if (connection.IsCreated) {
                        SendMessageInternal(connection, messageId, writeMessageCallback, sendMode);
                    }
                }
            });
        }

        /// <summary>
        /// Send a message to every filtered connection.
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="writeMessageCallback"></param>
        /// <param name="filter"></param>
        /// <param name="sendMode"></param>
        public static void SendMessageToFiltered(byte messageId, WriteMessageCallback writeMessageCallback, Func<NetworkConnection, bool> filter, SendMode sendMode = SendMode.Default) {
            mainThreadEventQueue.Enqueue(() => {
                foreach (NetworkConnection connection in connections) {
                    if (connection.IsCreated && filter(connection)) {
                        SendMessageInternal(connection, messageId, writeMessageCallback, sendMode);
                    }
                }
            });
        }

        private static void SendMessageInternal(NetworkConnection connection, byte messageId, WriteMessageCallback writeMessageCallback, SendMode sendMode) {
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
            if (!IsRunning) {
                return;
            }

            // Accept all incoming connections (todo: send to a pending queue for a password system)
            NetworkConnection connectionToAccept;
            while ((connectionToAccept = driver.Accept()) != default(NetworkConnection)) {
                connections.Add(connectionToAccept);
                Debug.Log($"[Server] Connection { connectionToAccept.GetHashCode() } has successfully connected!");
            }

            // Process all of the events in the driver's buffer
            NetworkEvent.Type networkEvent;
            while ((networkEvent = driver.PopEvent(out NetworkConnection connection, out DataStreamReader stream)) != NetworkEvent.Type.Empty) {
                switch (networkEvent) {

                    // Process the data from the incoming stream
                    case NetworkEvent.Type.Data: {
                            if (stream.Length > 0 && messageCallbacks.TryGetValue(stream.ReadByte(), out ReceiveMessageCallback callback)) {
                                callback(in connection, ref stream);
                            }
                            break;
                        }

                    // Process a connection's disconnection from the server
                    case NetworkEvent.Type.Disconnect: {
                            if (connections.Remove(connection)) {
                                Debug.Log($"[Server] Connection { connection.GetHashCode() } has successfully disconnected! Reason: { (DisconnectReason)stream.ReadByte() }");

                                // todo: notify all other connections
                            }
                            break;
                        }
                }
            }
        }

        private static void AfterUpdate() {
            if (!IsRunning) {
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
                Stop();
                NetcodeUtility.ResetSubsystems();
            };
            NetcodeUtility.InjectSubsystems(typeof(ServerManager), BeforeUpdate, AfterUpdate);
        }

    }
}
