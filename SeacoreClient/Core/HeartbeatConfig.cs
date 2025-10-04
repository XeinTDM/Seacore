namespace SeacoreClient.Core
{
    public class HeartbeatConfig
    {
        public int ExcellentInterval { get; set; } = 8000;
        public int GoodInterval { get; set; } = 15000;
        public int ModerateInterval { get; set; } = 30000;
        public int PoorInterval { get; set; } = 60000;
        public double JitterPercentage { get; set; } = 0.1;
        public int MaxConsecutiveFailures { get; set; } = 5;
    }
}
