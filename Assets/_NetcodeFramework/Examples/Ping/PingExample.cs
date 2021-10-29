using NetcodeFramework.Client;
using NetcodeFramework.Server;
using Unity.Networking.Transport;
using UnityEngine;

namespace NetcodeFramework.Examples.Ping {
    public class PingExample : MonoBehaviour {
        public const byte PING_ID = 1;

        [Min(1)]
        public int tickRate = 50;

        private void Awake() {
            Time.fixedDeltaTime = 1f / tickRate;
            ServerManager.RegisterMessage(PING_ID, (in NetworkConnection connection, ref DataStreamReader stream) => {
                float time = stream.ReadFloat();
                ServerManager.SendMessageTo(connection, PING_ID, (ref DataStreamWriter writer) => {
                    writer.WriteFloat(time);
                });
            });
            ClientManager.RegisterMessage(PING_ID, (ref DataStreamReader stream) => {
                float sendTime = stream.ReadFloat();
                float receiveTime = Time.fixedTime;
                float frametime = Time.fixedDeltaTime * 2;
                float rtt = receiveTime - sendTime;
                Debug.Log($"RTT={(int)(rtt*1000)}ms, RTT-FT={(int)((rtt-frametime)*1000)}ms, frametime={frametime*1000}ms, sent at {(int)(sendTime * 1000)}ms, received at {(int)(receiveTime*1000)}ms");
            }); 
        }

        private void Start() {
            ServerManager.Start(7777);
            ClientManager.Connect("127.0.0.1", 7777);
        }

        private void FixedUpdate() {
            if (ClientManager.ConnectionState == ConnectionState.Connected) {
                ClientManager.SendMessage(PING_ID, (ref DataStreamWriter stream) => {
                    stream.WriteFloat(Time.fixedTime);
                }, SendMode.Default);
            }
        }
    }
}