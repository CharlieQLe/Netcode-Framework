using NetcodeFramework.Client;
using NetcodeFramework.Server;
using Unity.Networking.Transport;
using UnityEngine;

namespace NetcodeFramework.Examples.Ping {
    public class PingExample : MonoBehaviour {
        public const byte PING_ID = 1;

        private void Awake() {
            ServerManager.RegisterMessage(PING_ID, (in NetworkConnection connection, ref DataStreamReader stream) => {
                float time = stream.ReadFloat();
                ServerManager.SendMessageTo(connection, PING_ID, (ref DataStreamWriter writer) => {
                    writer.WriteFloat(time);
                });
            });
            ClientManager.RegisterMessage(PING_ID, (ref DataStreamReader stream) => {
                float sendTime = stream.ReadFloat();
                float receiveTime = Time.time;
                int rtt = (int)((receiveTime - sendTime) * 1000);
                Debug.Log($"RTT={rtt}ms, sent at {(int)(sendTime * 1000)}ms, received at {(int)(receiveTime*1000)}ms");
            }); 
        }

        private void Start() {
            ServerManager.Start(7777);
            ClientManager.Connect("127.0.0.1", 7777);
        }

        private void OnDestroy() {
            ServerManager.Stop();
        }

        private void FixedUpdate() {
            if (ClientManager.ConnectionState == ConnectionState.Connected) {
                ClientManager.SendMessage(PING_ID, (ref DataStreamWriter stream) => {
                    stream.WriteFloat(Time.time);
                }, SendMode.Default);
            }
        }
    }
}