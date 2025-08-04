namespace SeacoreClient.Core
{
    public class HeartbeatConfig
    {
        public int ExcellentInterval { get; set; }
        public int GoodInterval { get; set; }
        public int ModerateInterval { get; set; }
        public int PoorInterval { get; set; }
        public double JitterPercentage { get; set; }
        public int MaxConsecutiveFailures { get; set; }
    }
}
