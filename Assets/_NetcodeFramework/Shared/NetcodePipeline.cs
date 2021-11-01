using Unity.Networking.Transport;

namespace NetcodeFramework {
    public class NetcodePipeline {
        private NetworkPipeline sequencedPipeline;
        private NetworkPipeline reliablePipeline;

        public NetworkPipeline SequencedPipeline => sequencedPipeline;
        public NetworkPipeline ReliablePipeline => reliablePipeline;

        public NetcodePipeline(NetworkDriver driver) {
            sequencedPipeline = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            reliablePipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
        }

        public NetworkPipeline GetPipeline(SendMode sendMode) {
            switch (sendMode) {
                case SendMode.Sequenced: return sequencedPipeline;
                case SendMode.Reliable: return reliablePipeline;
                default: return NetworkPipeline.Null;
            }
        }
    }
}
