namespace SeacoreClient.Core
{
    public class ServerConfig
    {
        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 2332;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                throw new InvalidOperationException("Server host must be provided.");
            }

            if (Port <= 0 || Port > 65535)
            {
                throw new InvalidOperationException("Server port must be between 1 and 65535.");
            }
        }
    }
}
